﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhino.Queues.Monitoring
{
    public class PerformanceMonitor
    {
        private readonly IQueueManager queueManager;

        public PerformanceMonitor(IQueueManager queueManager)
        {
            this.queueManager = queueManager;
            AttachToEvents();
            SyncWithCurrentQueueState();
        }

        private void SyncWithCurrentQueueState()
        {
            foreach (var queueName in queueManager.Queues)
            {
                var queueCount = queueManager.GetNumberOfMessages(queueName);

                foreach (var subQueue in queueManager.GetSubqueues(queueName))
                {
                    //HACK: There is currently no direct way to get just a count of messages in a subqueue
                    var count = queueManager.GetAllMessages(queueName,subQueue).Length;
                    queueCount -= count;
                    var counters = GetInboundCounters(queueManager.InboundInstanceName(queueName, subQueue));
                    lock (counters)
                    {
                        counters.ArrivedMessages = count;
                    }
                }

                var queueCounters = GetInboundCounters(queueManager.InboundInstanceName(queueName, string.Empty));
                lock (queueCounters)
                {
                    queueCounters.ArrivedMessages = queueCount;
                }
            }

            foreach (var counter in from m in queueManager.GetMessagesCurrentlySending()
                                      group m by m.Endpoint.OutboundInstanceName(m) into c
                                      select new {InstanceName = c.Key, Count = c.Count()})
            {
                var outboundCounter = GetOutboundCounters(counter.InstanceName);
                lock (outboundCounter)
                {
                    outboundCounter.UnsentMessages = counter.Count;
                }
            }
        }

        private void AttachToEvents()
        {
            queueManager.MessageQueuedForSend += OnMessageQueuedForSend;
            queueManager.MessageSent += OnMessageSent;
            queueManager.MessageQueuedForReceive += OnMessageQueuedForReceive;
            queueManager.MessageReceived += OnMessageReceived;
        }

        private void OnMessageQueuedForSend(object source, MessageEventArgs e)
        {
            var counters = GetOutboundCounters(e);
            lock(counters)
            {
                counters.UnsentMessages++;
            }
        }

        private void OnMessageSent(object source, MessageEventArgs e)
        {
            var counters = GetOutboundCounters(e);
            lock (counters)
            {
                counters.UnsentMessages--;
            }
        }

        private void OnMessageQueuedForReceive(object source, MessageEventArgs e)
        {
            var counters = GetInboundCounters(e);
            lock (counters)
            {
                counters.ArrivedMessages++;
            }
        }

        private void OnMessageReceived(object source, MessageEventArgs e)
        {
            var counters = GetInboundCounters(e);
            lock (counters)
            {
                counters.ArrivedMessages--;
            }
        }

        private readonly Dictionary<string, IOutboundPerfomanceCounters> outboundCounters = new Dictionary<string, IOutboundPerfomanceCounters>();
        private IOutboundPerfomanceCounters GetOutboundCounters(MessageEventArgs e)
        {
            var instanceName = e.Endpoint.OutboundInstanceName(e.Message);

            IOutboundPerfomanceCounters counter;
            if (!outboundCounters.TryGetValue(instanceName, out counter))
            {
                lock (outboundCounters)
                {
                    if (!outboundCounters.TryGetValue(instanceName, out counter))
                    {
                        counter = GetOutboundCounters(instanceName);
                        outboundCounters.Add(instanceName, counter);
                    }
                }
            }
            return counter;
        }

        protected virtual IOutboundPerfomanceCounters GetOutboundCounters(string instanceName)
        {
            return new OutboundPerfomanceCounters(instanceName);
        }

        private readonly Dictionary<string, IInboundPerfomanceCounters> inboundCounters = new Dictionary<string, IInboundPerfomanceCounters>();
        private IInboundPerfomanceCounters GetInboundCounters(MessageEventArgs e)
        {
            var instanceName = queueManager.InboundInstanceName(e.Message);

            IInboundPerfomanceCounters counter;
            if (!inboundCounters.TryGetValue(instanceName, out counter))
            {
                lock (outboundCounters)
                {
                    if (!inboundCounters.TryGetValue(instanceName, out counter))
                    {
                        counter = GetInboundCounters(instanceName);
                        inboundCounters.Add(instanceName, counter);
                    }
                }
            }
            return counter;
        }

        protected virtual IInboundPerfomanceCounters GetInboundCounters(string instanceName)
        {
            return new InboundPerfomanceCounters(instanceName);
        }
    }
}