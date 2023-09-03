using AlbionData.Models;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NATS.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace albiondata_sql_dotNet
{
  internal class Program
  {
    private static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

    [Option(Description = "NATS Public Url", ShortName = "n", ShowInHelpText = true)]
    public static string NatsPublicUrl { get; set; } = "nats://public:thenewalbiondata@nats.albion-online-data.com:4222";

    [Option(Description = "NATS Private Url", ShortName = "np", ShowInHelpText = true)]
    public static string NatsPrivateUrl { get; set; } = "nats://localhost:4222";

    [Option(Description = "SQL Connection Url", ShortName = "s", ShowInHelpText = true)]
    public static string SqlConnectionUrl { get; set; } = "data source=ESCRITORIO;initial catalog=albiondata;TrustServerCertificate=True;trusted_connection=true";

    [Option(Description = "Check Every x Minutes for expired orders", ShortName = "e", ShowInHelpText = true)]
    [Range(1, 1440)]
    public static int ExpireCheckMinutes { get; set; } = 60;

    [Option(Description = "Max age in Hours that orders exist before deletion", ShortName = "a", ShowInHelpText = true)]
    [Range(1, 168)]
    public static int MaxAgeHours { get; set; } = 24;

    [Option(Description = "Enable Debug Logging", ShortName = "d", LongName = "debug", ShowInHelpText = true)]
    public static bool Debug { get; set; }

    public static ILoggerFactory Logger { get; } = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(Debug ? LogLevel.Debug : LogLevel.Information));
    public static ILogger CreateLogger<T>() => Logger.CreateLogger<T>();

    private static readonly ManualResetEvent quitEvent = new ManualResetEvent(false);

    private static ulong updatedOrderCounter;
    private static ulong updatedHistoryCounter;
    private static ulong updatedGoldCounter;

    private static readonly Timer expirationTimer = new Timer(ExpirationTask, null, Timeout.Infinite, Timeout.Infinite);

    #region Connections
    private static readonly Lazy<IConnection> lazyNatsPublic = new Lazy<IConnection>(() =>
    {
      var natsFactory = new ConnectionFactory();
      if (NatsPublicUrl != string.Empty)
      {
        return natsFactory.CreateConnection(NatsPublicUrl);
      }
      else return null;
    });

    public static IConnection NatsPublicConnection
    {
      get
      {
        return lazyNatsPublic.Value;
      }
    }
    private static readonly Lazy<IConnection> lazyNatsPrivate = new Lazy<IConnection>(() =>
    {
      var natsFactory = new ConnectionFactory();
      if (NatsPrivateUrl != string.Empty)
      {
        return natsFactory.CreateConnection(NatsPrivateUrl);
      }
      else return null;
    });

    public static IConnection NatsPrivateConnection
    {
      get
      {
        return lazyNatsPrivate.Value;
      }
    }
    #endregion
    #region Subjects
    private const string marketOrdersDedupedBulk = "marketorders.deduped.bulk";
    private const string marketHistoriesDeduped = "markethistories.deduped";
    private const string goldDataDeduped = "goldprices.deduped";
    #endregion

    private void OnExecute()
    {
      Console.CancelKeyPress += (sender, args) =>
      {
        quitEvent.Set();
        args.Cancel = true;
      };

      var logger = CreateLogger<Program>();
      if (Debug)
      {
        logger.LogInformation("Debugging enabled");
      }

      using (var context = new ConfiguredContext())
      {
        if (context.Database.EnsureCreated())
        {
          logger.LogInformation("Database Created");
          context.SaveChanges();
        }
        else
        {
          logger.LogInformation("Database Exists");
        }
      }

      if (NatsPublicConnection != null)
      {
        logger.LogInformation($"Nats Public URL: {NatsPublicUrl}");
        logger.LogInformation($"NATS Public Connected, ID: {NatsPublicConnection?.ConnectedId}");

        var incomingPublicMarketOrders = NatsPublicConnection.SubscribeAsync(marketOrdersDedupedBulk);
        var incomingPublicMarketHistories = NatsPublicConnection.SubscribeAsync(marketHistoriesDeduped);
        var incomingPublicGoldData = NatsPublicConnection.SubscribeAsync(goldDataDeduped);

        incomingPublicMarketOrders.MessageHandler += HandleMarketOrderBulk;
        incomingPublicMarketHistories.MessageHandler += HandleMarketHistory;
        incomingPublicGoldData.MessageHandler += HandleGoldData;

        incomingPublicMarketOrders.Start();
        logger.LogInformation("Listening for Market Order Data in Public Nats");
        incomingPublicMarketHistories.Start();
        logger.LogInformation("Listening for Market History Data in Public Nats");
        incomingPublicGoldData.Start();
        logger.LogInformation("Listening for Gold Data in Public Nats");
      }

      if (NatsPrivateConnection != null)
      {
        logger.LogInformation($"Nats Private URL: {NatsPrivateUrl}");
        logger.LogInformation($"NATS Private Connected, ID: {NatsPrivateConnection.ConnectedId}");

        var incomingPrivateMarketOrders = NatsPrivateConnection.SubscribeAsync(marketOrdersDedupedBulk);
        var incomingPrivateMarketHistories = NatsPrivateConnection.SubscribeAsync(marketHistoriesDeduped);
        var incomingPrivateGoldData = NatsPrivateConnection.SubscribeAsync(goldDataDeduped);

        incomingPrivateMarketOrders.MessageHandler += HandleMarketOrderBulk;
        incomingPrivateMarketHistories.MessageHandler += HandleMarketHistory;
        incomingPrivateGoldData.MessageHandler += HandleGoldData;

        incomingPrivateMarketOrders.Start();
        logger.LogInformation("Listening for Market Order Data in Private Nats");
        incomingPrivateMarketHistories.Start();
        logger.LogInformation("Listening for Market History Data in Private Nats");
        incomingPrivateGoldData.Start();
        logger.LogInformation("Listening for Gold Data in Private Nats");

        logger.LogInformation($"Checking Every {ExpireCheckMinutes} Minutes for expired orders.");
        logger.LogInformation($"Deleting orders after {MaxAgeHours} hours");
      }

      expirationTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(ExpireCheckMinutes));

      quitEvent.WaitOne();
      NatsPublicConnection?.Close();
      NatsPrivateConnection?.Close();
    }

    private static void HandleMarketOrderBulk(object sender, MsgHandlerEventArgs args)
    {
      var logger = CreateLogger<Program>();
      var message = args.Message;
      try
      {
        using var context = new ConfiguredContext();
        var marketOrderUpdates = JsonConvert.DeserializeObject<List<MarketOrderDB>>(Encoding.UTF8.GetString(message.Data));

        // The database calls the unique Albion ID AlbionId
        // However the data client sends this as just Id
        // Transfer Id data to AlbionId to match database naming
        foreach (var marketOrderUpdate in marketOrderUpdates)
        {
          marketOrderUpdate.AlbionId = marketOrderUpdate.Id;
          marketOrderUpdate.Id = 0;
        }

        // AlbionId is always unique
        var dbOrders = context.MarketOrders
          .Where(x => marketOrderUpdates.Select(y => y.AlbionId).Contains(x.AlbionId))
          .ToDictionary(x => x.AlbionId);

        foreach (var marketOrderUpdate in marketOrderUpdates)
        {
          dbOrders.TryGetValue(marketOrderUpdate.AlbionId, out var dbOrder);
          if (dbOrder != null)
          {
            dbOrder.UnitPriceSilver = marketOrderUpdate.UnitPriceSilver;
            dbOrder.UpdatedAt = DateTime.UtcNow;
            dbOrder.Amount = marketOrderUpdate.Amount;
            dbOrder.LocationId = marketOrderUpdate.LocationId;
            dbOrder.DeletedAt = null;
            context.MarketOrders.Update(dbOrder);
          }
          else
          {
            marketOrderUpdate.InitialAmount = marketOrderUpdate.Amount;
            marketOrderUpdate.CreatedAt = DateTime.UtcNow;
            marketOrderUpdate.UpdatedAt = DateTime.UtcNow;
            if (marketOrderUpdate.Expires > DateTime.UtcNow.AddYears(1))
            {
              marketOrderUpdate.Expires = DateTime.UtcNow.AddDays(7);
            }
            context.MarketOrders.Add(marketOrderUpdate);
          }
          updatedOrderCounter++;
        }
        context.SaveChanges();
        logger.LogInformation(GetLogMessage(marketOrderUpdates.Count, "Market Orders", updatedOrderCounter));
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error handling market order");
      }
    }

    private static void HandleMarketHistory(object sender, MsgHandlerEventArgs args)
    {
      var logger = CreateLogger<Program>();
      var message = args.Message;
      try
      {
        using var context = new ConfiguredContext();
        var upload = JsonConvert.DeserializeObject<MarketHistoriesUpload>(Encoding.UTF8.GetString(message.Data));
        var aggregationType = TimeAggregation.QuarterDay;
        if (upload.Timescale == Timescale.Day)
        {
          aggregationType = TimeAggregation.Hourly;
        }

        // Do not use the last history timestamp because it is a partial period
        // It is not guaranteed to be updated so it can appear that the count in the period was way lower
        var marketHistoryUpdates = upload.MarketHistories.OrderBy(x => x.Timestamp).SkipLast(1);

        var dbHistories = context.MarketHistories.Where(x =>
           x.ItemTypeId == upload.AlbionIdString
        && x.Location == upload.LocationId
        && x.QualityLevel == upload.QualityLevel
        && marketHistoryUpdates.Select(x => new DateTime((long)x.Timestamp)).Contains(x.Timestamp)
        && x.AggregationType == aggregationType)
          .ToDictionary(x => x.Timestamp);

        foreach (var marketHistoryUpdate in marketHistoryUpdates)
        {
          var historyDate = new DateTime((long)marketHistoryUpdate.Timestamp);
          dbHistories.TryGetValue(historyDate, out var dbHistory);
          if (dbHistory == null)
          {
            dbHistory = new MarketHistoryDB
            {
              AggregationType = aggregationType,
              ItemTypeId = upload.AlbionIdString,
              Location = upload.LocationId,
              QualityLevel = upload.QualityLevel,
              ItemAmount = marketHistoryUpdate.ItemAmount,
              SilverAmount = marketHistoryUpdate.SilverAmount,
              Timestamp = historyDate
            };

            context.MarketHistories.Add(dbHistory);
          }
          else
          {
            dbHistory.ItemAmount = marketHistoryUpdate.ItemAmount;
            dbHistory.SilverAmount = marketHistoryUpdate.SilverAmount;
            context.MarketHistories.Update(dbHistory);
          }
          updatedHistoryCounter++;
        }
        context.SaveChanges();
        logger.LogInformation(GetLogMessage(marketHistoryUpdates.Count(), "Market Histories", updatedHistoryCounter));
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error handling market history");
      }
    }

    private static void ExpirationTask(object state)
    {
      try
      {
        var start = DateTime.Now;
        File.AppendAllText("last-run.txt", start.ToString("F") + Environment.NewLine);

        var logger = CreateLogger<Program>();
        logger.LogInformation("Expiration Task Starting");
        using var context = new ConfiguredContext();
        const int batchSize = 1000;
        var sleepTime = TimeSpan.FromSeconds(10);

        // Using int.MaxValue allows us to use the rows affected from the previous delete to determine if we should keep deleting
        // We shouldn't keep deleting if the last delete did not delete a full batch
        var lastDeletedOrderCount = int.MaxValue;
        var lastDeletedHistoryCount = int.MaxValue;
        var totalCount = 0;
        var changesLeft = true;
        while (changesLeft)
        {
          changesLeft = false;
          // Delete old market orders
          if (lastDeletedOrderCount >= batchSize)
          {
            lastDeletedOrderCount = context.Database.ExecuteSqlRaw(@$"DELETE
FROM market_orders
WHERE deleted_at IS NULL
AND
(
expires < UTC_TIMESTAMP()
OR
updated_at < DATE_ADD(UTC_TIMESTAMP(), INTERVAL -{MaxAgeHours} HOUR)
)
LIMIT {batchSize}");
            totalCount += lastDeletedOrderCount;
            logger.LogInformation($"Expired {lastDeletedOrderCount} Market Orders. Total Order/History Expiration: {totalCount}");
            Thread.Sleep(sleepTime);
          }

          // Delete old hourly history records
          if (lastDeletedHistoryCount >= batchSize)
          {
            lastDeletedHistoryCount = context.Database.ExecuteSqlRaw(@$"DELETE
FROM market_history
WHERE aggregation = 1
AND timestamp < DATE_ADD(UTC_TIMESTAMP(), INTERVAL -7 DAY)
LIMIT {batchSize}");
            totalCount += lastDeletedHistoryCount;
            logger.LogInformation($"Expired {lastDeletedHistoryCount} Market Histories. Total Order/History Expiration: {totalCount}");
            Thread.Sleep(sleepTime);
          }

          // Keep expiring when we are expiring large numbers at a time
          // Stop expiring when at less than a full batch
          if (lastDeletedOrderCount >= batchSize || lastDeletedHistoryCount >= batchSize)
          {
            changesLeft = true;
          }

          // We have been deleting for too long, kill this thread
          if ((DateTime.Now - start).TotalMinutes > ExpireCheckMinutes * 0.75)
          {
            logger.LogWarning("Killing Long Running Expiration Task");
            changesLeft = false;
          }
        }
        logger.LogInformation($"Total Order/History Expirations: {totalCount} Time Spent: {DateTime.Now - start}");
      }
      catch (Exception ex)
      {
        File.AppendAllText("last-run.txt", DateTime.Now.ToString("F") + Environment.NewLine + ex.ToString() + Environment.NewLine);
      }
    }

    private static void HandleGoldData(object sender, MsgHandlerEventArgs args)
    {
      var logger = CreateLogger<Program>();
      var message = args.Message;
      try
      {
        var upload = JsonConvert.DeserializeObject<GoldPriceUpload>(Encoding.UTF8.GetString(message.Data));
        if (upload.Prices.Length != upload.Timestamps.Length)
        {
          throw new Exception("Different list lengths");
        }

        using var context = new ConfiguredContext();
        for (var i = 0; i < upload.Prices.Length; i++)
        {
          var price = upload.Prices[i];
          var timestamp = new DateTime(upload.Timestamps[i], DateTimeKind.Utc);
          var dbGold = context.GoldPrices.FirstOrDefault(x => x.Timestamp == timestamp);
          if (dbGold != null)
          {
            if (dbGold.Price != price)
            {
              dbGold.Price = price;
              context.GoldPrices.Update(dbGold);
            }
          }
          else
          {
            var goldPrice = new GoldPrice()
            {
              Price = price,
              Timestamp = timestamp
            };
            context.GoldPrices.Add(goldPrice);
          }
          updatedGoldCounter++;
        }
        context.SaveChanges();
        logger.LogInformation(GetLogMessage(upload.Prices.Length, "Gold Records", updatedGoldCounter));
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error handling gold data");
      }
    }

    private static string GetLogMessage(int updateCount, string updateType, ulong totalProcessed)
    {
      return $"Updated/Created {updateCount,3} {$"{updateType}.",-18} Total Processed: {totalProcessed,10}";
    }
  }
}
