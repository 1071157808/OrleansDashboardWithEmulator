﻿using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace OrleansDashboard
{
    public sealed class DashboardTelemetryProducer : ITelemetryProducer, IDisposable
    {
        public class Value<T>
        {
            public T Current;
            public T Last;

            public Value(T value)
                : this(value, value)
            {

            }

            public Value(T last, T current)
            {
                Last = last;

                Current = current;
            }

            public Value<T> Update(T newValue)
            {
                return new Value<T>(Current, newValue);
            }
        }

        private readonly ConcurrentDictionary<string, Value<double>> metrics = new ConcurrentDictionary<string, Value<double>>();
        private readonly ConcurrentDictionary<string, Value<TimeSpan>> timespanMetrics = new ConcurrentDictionary<string, Value<TimeSpan>>();
        private readonly ILocalSiloDetails localSiloDetails;
        private readonly IGrainFactory grainFactory;
        private readonly IExternalDispatcher dispatcher;
        private readonly Timer timer;
        private bool isClosed;

        public DashboardTelemetryProducer(ILocalSiloDetails localSiloDetails, IGrainFactory grainFactory, IExternalDispatcher dispatcher)
        {
            this.localSiloDetails = localSiloDetails;
            this.grainFactory = grainFactory;
            this.dispatcher = dispatcher;

            // register timer to report every second
            timer = new Timer(x => Flush(), null, 1 * 1000, 1 * 1000);
        }

        public void DecrementMetric(string name)
        {
            DecrementMetric(name, 1);
        }

        public void IncrementMetric(string name)
        {
            IncrementMetric(name, 1);
        }

        public void DecrementMetric(string name, double value)
        {
            IncrementMetric(name, -value);
        }

        public void TrackMetric(string name, double value, IDictionary<string, string> properties = null)
        {
            metrics.AddOrUpdate(name, new Value<double>(value), (key, current) => current.Update(value));
        }

        public void IncrementMetric(string name, double value)
        {
            metrics.AddOrUpdate(name, new Value<double>(value), (key, current) => current.Update(current.Current + value));
        }

        public void TrackMetric(string name, TimeSpan value, IDictionary<string, string> properties = null)
        {
            timespanMetrics.AddOrUpdate(name, new Value<TimeSpan>(value), (key, current) => current.Update(value));
        }

        public void Flush()
        {
            if (dispatcher.CanDispatch())
            {
                var grain = grainFactory.GetGrain<ISiloGrain>(localSiloDetails.SiloAddress.ToParsableString());

                var counters = new List<StatCounter>();

                foreach (var metric in metrics.ToArray())
                {
                    var v = metric.Value.Current;
                    var d = metric.Value.Current - metric.Value.Last;

                    counters.Add(new StatCounter { Name = metric.Key, Value = v.ToString(CultureInfo.InvariantCulture), Delta = d.ToString(CultureInfo.InvariantCulture) });
                }

                foreach (var metric in timespanMetrics.ToArray())
                {
                    var v = metric.Value.Current;
                    var d = metric.Value.Current - metric.Value.Last;

                    counters.Add(new StatCounter { Name = metric.Key, Value = v.ToString("c", CultureInfo.InvariantCulture), Delta = d.ToString("c", CultureInfo.InvariantCulture) });
                }

                if (counters.Count > 0)
                {
                    var countersArray = counters.ToArray();

                    dispatcher.DispatchAsync(() => grain.ReportCounters(countersArray));
                }
            }
        }

        public void Close()
        {
            if (!isClosed)
            {
                isClosed = true;

                timer.Dispose();
            }
        }

        public void Dispose()
        {
            Close();
        }

        [Obsolete]
        public void TrackDependency(string name, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success) { }

        [Obsolete]
        public void TrackEvent(string name, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null) { }

        [Obsolete]
        public void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success) { }

        [Obsolete]
        public void TrackException(Exception exception, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null) { }

        [Obsolete]
        public void TrackTrace(string message) { }

        [Obsolete]
        public void TrackTrace(string message, Severity severityLevel) { }

        [Obsolete]
        public void TrackTrace(string message, Severity severityLevel, IDictionary<string, string> properties) { }

        [Obsolete]
        public void TrackTrace(string message, IDictionary<string, string> properties) { }
    }
}
