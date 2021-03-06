using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperSocket.Config;

namespace SuperSocket.Server
{
    public class SocketServer
    {
        private IServiceCollection _serviceCollection;

        private IServiceProvider _serviceProvider;

        public ServerConfig Config { get; private set; }

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// Server's name
        /// </summary>
        /// <returns>the name of the server instance</returns>
        public string Name 
        {
            get { return Config.Name; }
        }

        public Listener[] Listeners { get; private set; }

        protected internal ILoggerFactory LoggerFactory { get; private set; }

        private ILogger _logger;

        private bool _initialized = false;

        public bool Configure(IConfiguration config)
        {
            var services = new ServiceCollection();
            return Configure(services, config);
        }

        public bool Configure(IServiceCollection services, IConfiguration config)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (config == null)
                throw new ArgumentNullException(nameof(config));
            
            // prepare service collections
            _serviceCollection = services.AddOptions() // activate options
                .AddLogging() // add logging
                .Configure<ServerConfig>(config);

            // build service provider
            _serviceProvider = services.BuildServiceProvider();

            // get server config
            var serverConfigOption = _serviceProvider.GetService<IOptions<ServerConfig>>();

            if (serverConfigOption == null || serverConfigOption.Value == null)
            {
                throw new ArgumentException("Invalid configuration", nameof(config));
            }

            Config = serverConfigOption.Value;

            // initialize logger factory
            LoggerFactory = _serviceProvider
                    .GetService<ILoggerFactory>()
                    .AddConsole(LogLevel.Debug);

            _logger = LoggerFactory.CreateLogger("SocketServer");

            ConfigureListeners(Config);

            return _initialized = true;
        }

        private void ConfigureListeners(ServerConfig serverConfig)
        {
            Listeners = serverConfig
                .Listeners
                .Select(l => new Listener(l)).ToArray();
        }

        public bool Start()
        {
            if (!_initialized)
                throw new Exception("The server has not been initialized successfully!");

            var listenSockets = new List<SocketListener>(Listeners.Length);
            
            foreach (var listener in Listeners)
            {
                var listenSocket = new SocketListener(listener);

                try
                {
                    listenSocket.StartListen();
                    _logger.LogDebug($"Listen the endpoint {listener.EndPoint} suceeded.");
                }
                catch (Exception e)
                {
                    _logger.LogError($"Listen the endpoint {listener.EndPoint} failed.", e);
                    continue;
                }
                
                listenSockets.Add(listenSocket);
            }

            if (!listenSockets.Any())
            {
                _logger.LogError($"No listener was started!");
                return false;
            }

            foreach (var listener in listenSockets)
            {
                AcceptClients(listener);
            }

            return true;
        }

        private void OnNewClientAccept(Socket client)
        {

        }

        private async void AcceptClients(SocketListener socketListener)
        {
            _logger.LogDebug($"The listener {socketListener.Listener.EndPoint} started accepting new clients.");

            var token = _cancellationTokenSource.Token;
            
            while (!token.IsCancellationRequested)
            {
                var socket = await socketListener.AcceptAsync();

                OnNewClientAccept(socket);

                if (token.IsCancellationRequested)
                    break;
            }

            _logger.LogDebug($"The listener {socketListener.Listener.EndPoint} was stopped.");
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }
    }
}