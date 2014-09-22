﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Quartz.Core;
using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Tests.Integration.Impl.AdoJobStore;

namespace Quartz.Impl.MongoDB.Tests.UnitTests
{
    [TestFixture]
    public class MongoJobStoreTests : AbstractMongoJobStoreTests
    {
        public class SampleJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                Thread.Sleep(50000);
            }
        }

        public class TestStdSchedulerFactory : StdSchedulerFactory
        {
            public TestStdSchedulerFactory(NameValueCollection props) : base(props)
            {
            }

            protected override IScheduler Instantiate(QuartzSchedulerResources rsrcs, QuartzScheduler qs)
            {
                this.Resources = rsrcs;
                return base.Instantiate(rsrcs, qs);
            }

            public QuartzSchedulerResources Resources { get; private set; }
        }

        [Test]
        public void shutdown_should_release_acquired_triggers()
        {
            var sf = new TestStdSchedulerFactory(BuildProperties());
            var scheduler = sf.GetScheduler();

            scheduler.Clear();

            ScheduleSampleJob(scheduler, 1);
            ScheduleSampleJob(scheduler, 2);

            var jobStore = sf.Resources.JobStore;

            var triggers = jobStore.AcquireNextTriggers(
                DateTimeOffset.UtcNow.AddDays(1), 
                100, 
                TimeSpan.FromMinutes(10)
            );

            Assert.AreEqual(2, triggers.Count);

            scheduler.Shutdown();

            var triggersCollection = (MongoCollection)
                jobStore.GetType()
                    .GetProperty("Triggers", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(jobStore, null);
                    
            foreach (var t in triggers)
            {
                var trigger = triggersCollection.FindOneByIdAs<BsonDocument>(t.Key.ToBsonDocument());
                Assert.AreEqual("Waiting", trigger["State"].AsString);
            }

            Assert.IsEmpty(FailFastLoggerFactoryAdapter.Errors, "Found error from logging output");
        }

        static void ScheduleSampleJob(IScheduler scheduler, int id)
        {
            var job = new JobDetailImpl("job_"+id, "mongo", typeof (SampleJob))
            {
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                RequestsRecovery = true
            };

            IOperableTrigger trigger = new SimpleTriggerImpl("trig_"+id, "mongo", 20, TimeSpan.FromSeconds(5));
            trigger.EndTimeUtc = DateTime.UtcNow.AddYears(10);
            trigger.StartTimeUtc = DateTime.Now;
            scheduler.ScheduleJob(job, trigger);
        }
    }
}
