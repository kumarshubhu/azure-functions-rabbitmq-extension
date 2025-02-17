﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.Azure.WebJobs.Extensions.RabbitMQ
{
    [Extension("RabbitMQ")]
    internal class RabbitMQExtensionConfigProvider : IExtensionConfigProvider
    {
        private readonly IOptions<RabbitMQOptions> _options;
        private readonly INameResolver _nameResolver;
        private readonly IRabbitMQServiceFactory _rabbitMQServiceFactory;
        private ILogger _logger;
        private readonly IConfiguration _configuration;

        public RabbitMQExtensionConfigProvider(IOptions<RabbitMQOptions> options, INameResolver nameResolver, IRabbitMQServiceFactory rabbitMQServiceFactory, ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _options = options;
            _nameResolver = nameResolver;
            _rabbitMQServiceFactory = rabbitMQServiceFactory;
            _logger = loggerFactory?.CreateLogger(LogCategories.CreateTriggerCategory("RabbitMQ"));
            _configuration = configuration;
        }

        public void Initialize(ExtensionConfigContext context)
        {

            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var rule = context.AddBindingRule<RabbitMQAttribute>();
            rule.AddValidator(ValidateBinding);
            rule.BindToCollector<byte[]>((attr) =>
            {
                return new RabbitMQAsyncCollector(CreateContext(attr), _logger);
            });
            rule.BindToInput<IModel>(new RabbitMQClientBuilder(this, _options));
            rule.AddConverter<string, byte[]>(msg => Encoding.UTF8.GetBytes(msg));
            rule.AddOpenConverter<OpenType.Poco, byte[]>(typeof(PocoToBytesConverter<>));

            var triggerRule = context.AddBindingRule<RabbitMQTriggerAttribute>();
            triggerRule.BindToTrigger<BasicDeliverEventArgs>(new RabbitMQTriggerAttributeBindingProvider(
                    _nameResolver,
                    this,
                    _logger,
                    _options,
                    _configuration));

            // Converts BasicDeliverEventArgs to string so user can extract received message.
            triggerRule.AddConverter<BasicDeliverEventArgs, string>(args => Encoding.UTF8.GetString(args.Body))
                .AddConverter<BasicDeliverEventArgs, DirectInvokeString>((args) => new DirectInvokeString(null));

            // Convert BasicDeliverEventArgs --> string-- > JSON-- > POCO
            triggerRule.AddOpenConverter<BasicDeliverEventArgs, OpenType.Poco>(typeof(BasicDeliverEventArgsToPocoConverter<>), _logger);
        }

        public void ValidateBinding(RabbitMQAttribute attribute, Type type)
        {
            string connectionString = Utility.FirstOrDefault(attribute.ConnectionStringSetting, _options.Value.ConnectionString);
            string hostName = Utility.FirstOrDefault(attribute.HostName, _options.Value.HostName) ?? Constants.LocalHost;
            _logger.LogInformation("Setting hostName to localhost since it was not specified");
            string queueName = Utility.FirstOrDefault(attribute.QueueName, _options.Value.QueueName);

            string userName = Utility.FirstOrDefault(attribute.UserName, _options.Value.UserName);
            string password = Utility.FirstOrDefault(attribute.Password, _options.Value.Password);

            if (string.IsNullOrEmpty(connectionString) && !Utility.ValidateUserNamePassword(userName, password, hostName))
            {
                throw new InvalidOperationException("RabbitMQ username and password required if not connecting to localhost");
            }
        }

        internal RabbitMQContext CreateContext(RabbitMQAttribute attribute)
        {
            string connectionString = Utility.FirstOrDefault(attribute.ConnectionStringSetting, _options.Value.ConnectionString);
            string hostName = Utility.FirstOrDefault(attribute.HostName, _options.Value.HostName);
            string queueName = Utility.FirstOrDefault(attribute.QueueName, _options.Value.QueueName);
            string userName = Utility.FirstOrDefault(attribute.UserName, _options.Value.UserName);
            string password = Utility.FirstOrDefault(attribute.Password, _options.Value.Password);
            int port = Utility.FirstOrDefault(attribute.Port, _options.Value.Port);
            string deadLetterExchangeName = Utility.FirstOrDefault(attribute.DeadLetterExchangeName, _options.Value.DeadLetterExchangeName);


            RabbitMQAttribute resolvedAttribute;
            IRabbitMQService service;

            resolvedAttribute = new RabbitMQAttribute
            {
                ConnectionStringSetting = connectionString,
                HostName = hostName,
                QueueName = queueName,
                UserName = userName,
                Password = password,
                Port = port,
                DeadLetterExchangeName = deadLetterExchangeName,
            };

            service = GetService(connectionString, hostName, queueName, userName, password, port, deadLetterExchangeName);

            return new RabbitMQContext
            {
                ResolvedAttribute = resolvedAttribute,
                Service = service,
            };
        }

        internal IRabbitMQService GetService(string connectionString, string hostName, string queueName, string userName, string password, int port, string deadLetterExchangeName)
        {
            return _rabbitMQServiceFactory.CreateService(connectionString, hostName, queueName, userName, password, port, deadLetterExchangeName);
        }
        
         internal IRabbitMQService GetService(string connectionString, string hostName, string queueName, string userName, string password, int port, string deadLetterExchangeName, bool queueDurabe, bool deadLetterQueueDurable,string deadLetterQueuesuffix, string deadLetterExchangeType, string deadLetterRoutingKey)
        {
            return _rabbitMQServiceFactory.CreateService(connectionString, hostName, queueName, userName, password, port, deadLetterExchangeName, queueDurabe, deadLetterQueueDurable, deadLetterQueuesuffix, deadLetterExchangeType, deadLetterRoutingKey);
        }
        
        // Overloaded method used only for getting the RabbitMQ client
        internal IRabbitMQService GetService(string connectionString, string hostName, string userName, string password, int port)
        {
            return _rabbitMQServiceFactory.CreateService(connectionString, hostName, userName, password, port);
        }
    }
}
