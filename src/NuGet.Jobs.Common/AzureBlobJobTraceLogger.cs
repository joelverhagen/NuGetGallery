﻿using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Jobs.Common
{
    /// <summary>
    /// This logger helps log to Azure Blob Storage
    /// NOTE: For logging within this class which is rare, try using LogConsoleOnly method
    ///       such that any logging from within this class does not hit storage
    /// </summary>
    public sealed class AzureBlobJobTraceLogger : JobTraceLogger
    {
        // 'const's
        private const int MaxExpectedLogsPerRun = 1000000;
        private const int MaxLogBatchSize = 100;
        private const string JobLogNameFormat = "{0}/{1}.txt";
        private const string LogStorageContainerName = "ng-jobs-logs";

        // Static members
        private static ConcurrentQueue<ConcurrentQueue<string>> LogQueues;
        private static ConcurrentQueue<string> CurrentLogQueue;
        private static int JobLogBatchCounter;

        // For example, if MaxExpectedLogsPerRun is a million, and MaxLogBatchSize is 100, then maximum possible log batch counter would be 10000
        // That is a maximum of 5 digits. So, leadingzero specifier string would be "00000"
        // (Actually, 5 digits can handle upto 99999 which is roughly 10 times the expected. We are being, roughly, 10 times lenient)
        private static readonly string JobLogBatchCounterLeadingZeroSpecifier =
            new String('0',
                1 + Convert.ToInt32(
                        Math.Ceiling(
                            Math.Log10(
                                MaxExpectedLogsPerRun / MaxLogBatchSize))));

        // Instance members
        private CloudStorageAccount LogStorageAccount { get; set; }
        private CloudBlobContainer LogStorageContainer { get; set; }
        private string JobLogNamePrefix { get; set; }

        public AzureBlobJobTraceLogger(string jobName) : base(jobName)
        {
            Interlocked.Exchange(ref LogQueues, new ConcurrentQueue<ConcurrentQueue<string>>());
            Interlocked.Exchange(ref CurrentLogQueue, new ConcurrentQueue<string>());
            Interlocked.Exchange(ref JobLogBatchCounter, 0);

            var cstr = Environment.GetEnvironmentVariable(EnvironmentVariableKeys.StoragePrimary);
            if(cstr == null)
            {
                throw new ArgumentException("Storage Primary environment variable needs to be set to use Azure Storage for logging. Try passing '-consoleLogOnly' for avoiding this error");
            }

            LogStorageAccount = CloudStorageAccount.Parse(cstr);
            LogStorageContainer = LogStorageAccount.CreateCloudBlobClient().GetContainerReference(LogStorageContainerName);
            LogStorageContainer.CreateIfNotExists();

            var dt = DateTime.UtcNow;
            JobLogNamePrefix = String.Format("{0}/{1}/{2}", jobName, dt.ToString("yyyy/MM/dd/HH/mm/ss/fffffff"), Environment.MachineName);

            Task.Run(() => FlushRunner());
        }

        private void LogConsoleOnly(TraceEventType traceEventType, string message)
        {
            ConsoleColor currentConsoleForegroundColor = Console.ForegroundColor;
            ConsoleColor consoleForegroundColor;
            switch(traceEventType)
            {
                case TraceEventType.Critical:
                case TraceEventType.Error:
                    consoleForegroundColor = ConsoleColor.Red;
                    break;
                case TraceEventType.Warning:
                    consoleForegroundColor = ConsoleColor.Yellow;
                    break;
                case TraceEventType.Information:
                    consoleForegroundColor = ConsoleColor.DarkGreen;
                    break;
                case TraceEventType.Verbose:
                    consoleForegroundColor = ConsoleColor.DarkGray;
                    break;
                default:
                    consoleForegroundColor = ConsoleColor.White;
                    break;
            }
            Console.ForegroundColor = consoleForegroundColor;
            Console.WriteLine(MessageWithTraceEventType(traceEventType, message));
            Console.ForegroundColor = currentConsoleForegroundColor;
        }

        private void Log(TraceEventType traceEventType, string message)
        {
            LogConsoleOnly(traceEventType, message);
            QueueLog(
                MessageWithTraceEventType(
                    traceEventType,
                    message));
        }

        private void Log(TraceEventType traceEventType, string format, params object[] args)
        {
            var message = String.Format(format, args);
            Log(traceEventType, message);
        }

        private void QueueLog(string messageWithTraceLevel)
        {
            if(CurrentLogQueue == null)
            {
                // Consider using IDisposable pattern
                throw new ArgumentException("CurrentLogQueue cannot be null. It is likely that FlushAll has been called");
            }

            // FlushAll should never get called until after all the logging is done
            // This method is not thread-safe. If there was multi threading, CurrentLogQueue even after this point could be null
            // Let NullReferenceException be thrown, if this happens
            if(CurrentLogQueue.Count >= MaxLogBatchSize)
            {
                FlushCurrentQueue();
            }

            CurrentLogQueue.Enqueue(messageWithTraceLevel);
        }

        private void FlushRunner()
        {
            Thread.Sleep(1000);
            while (CurrentLogQueue != null)
            {
                Flush(skipCurrentBatch: true);
                Thread.Sleep(1000);
            }

            // Flush anything that may be pending due to timing
            Flush(skipCurrentBatch: true);

            // The following indicates that all the log flushing is done
            Interlocked.Exchange(ref JobLogBatchCounter, -1);
        }

        private void FlushCurrentQueue()
        {
            LogConsoleOnly(TraceEventType.Verbose, "Creating a new concurrent log queue...");
            LogQueues.Enqueue(Interlocked.Exchange(ref CurrentLogQueue, new ConcurrentQueue<string>()));
        }

        public override void Flush(bool skipCurrentBatch = false)
        {
            try
            {
                if(!skipCurrentBatch)
                {
                    FlushCurrentQueue();
                }

                ConcurrentQueue<string> headQueue;
                while (LogQueues.TryDequeue(out headQueue))
                {
                    string blobName = String.Format(JobLogNameFormat, JobLogNamePrefix, JobLogBatchCounter.ToString(JobLogBatchCounterLeadingZeroSpecifier));
                    Save(blobName, headQueue);
                    Interlocked.Increment(ref JobLogBatchCounter);
                }
            }
            catch (Exception ex)
            {
                LogConsoleOnly(TraceEventType.Error, ex.ToString());
            }
        }

        public override void FlushAll()
        {
            base.FlushAll();
            var logQueue = Interlocked.Exchange(ref CurrentLogQueue, null);
            if(logQueue != null)
            {
                LogQueues.Enqueue(logQueue);
            }

            while (JobLogBatchCounter != -1)
            {
                // Don't use the other overload of base.Log with format and args. That will call back into this class
                LogConsoleOnly(TraceEventType.Information, "Waiting for log flush runner loop to terminate...");
                Thread.Sleep(2000);
            }

            if(!LogQueues.IsEmpty)
            {
                throw new ArgumentException("LogQueues should be empty at the end of FlushAll...");
            }

            Interlocked.Exchange(ref LogQueues, null);
            LogConsoleOnly(TraceEventType.Information, "Successfully completed flushing of logs");
        }

        private void Save(string blobName, ConcurrentQueue<string> eventMessages)
        {
            StringBuilder builder = new StringBuilder();
            foreach(string eventMessage in eventMessages)
            {
                builder.AppendLine(eventMessage);
            }

            // Don't use the other overload of base.Log with format and args. That will call back into this class
            var blob = LogStorageContainer.GetBlockBlobReference(blobName);
            LogConsoleOnly(TraceEventType.Verbose, "Uploading to " + blob.Uri.ToString());
            blob.UploadText(builder.ToString());
        }

        public override void Write(string message)
        {
            WriteLine(message);
        }

        public override void WriteLine(string message)
        {
            Log(TraceEventType.Verbose, GetFormattedMessage(message));
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            Log(eventType, GetFormattedMessage(message));
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            TraceEvent(eventCache, source, eventType, id, String.Format(format, args));
        }

        public override void Fail(string message)
        {
            Log(TraceEventType.Critical, GetFormattedMessage(message));
        }

        public override void Fail(string message, string detailMessage)
        {
            Fail(message + ". Detailed: " + detailMessage);
        }
    }
}
