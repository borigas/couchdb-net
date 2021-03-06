﻿using CouchDB.Driver.Extensions;
using CouchDB.Driver.Helpers;
using CouchDB.Driver.Types;
using Flurl.Http;
using Flurl.Http.Configuration;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace CouchDB.Driver
{
    /// <summary>
    /// Client for querying a CouchDB database.
    /// </summary>
    public partial class CouchClient : IDisposable
    {
        private DateTime? _cookieCreationDate;
        private string _cookieToken;
        private readonly CouchSettings _settings;
        private readonly FlurlClient _flurlClient;
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Creates a new CouchDB client.
        /// </summary>
        /// <param name="connectionString">URI to the CouchDB endpoint.</param>
        /// <param name="couchSettingsFunc">A function to configure the client settings.</param>
        /// <param name="flurlSettingsFunc">A function to configure the HTTP client.</param>
        public CouchClient(string connectionString, Action<CouchSettings> couchSettingsFunc = null, Action<ClientFlurlHttpSettings> flurlSettingsFunc = null)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            _settings = new CouchSettings();
            couchSettingsFunc?.Invoke(_settings);

            ConnectionString = connectionString;
            _flurlClient = new FlurlClient(connectionString);

            _flurlClient.Configure(s =>
            {
                s.BeforeCall = OnBeforeCall;
                if (_settings.ServerCertificateCustomValidationCallback != null)
                {
                    s.HttpClientFactory = new CertClientFactory(_settings.ServerCertificateCustomValidationCallback);
                }

                flurlSettingsFunc?.Invoke(s);
            });
        }

        #region Operations

        #region CRUD

        /// <summary>
        /// Returns an instance of the CouchDB database with the given name. 
        /// If EnsureDatabaseExists is configured, it creates the database if it doesn't exists.
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <param name="database">The database name.</param>
        /// <returns>An instance of the CouchDB database with given name.</returns>
        public CouchDatabase<TSource> GetDatabase<TSource>(string database) where TSource : CouchEntity
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            if (_settings.CheckDatabaseExists)
            {
                var dbs = AsyncContext.Run(() => GetDatabasesNamesAsync());
                if (!dbs.Contains(database))
                {
                    return AsyncContext.Run(() => CreateDatabaseAsync<TSource>(database));
                }
            }

            return new CouchDatabase<TSource>(_flurlClient, _settings, ConnectionString, database);
        }
        /// <summary>
        /// Creates a new database with the given name in the server.
        /// The name must begin with a lowercase letter and can contains only lowercase characters, digits or _, $, (, ), +, - and /.s
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <param name="database">The database name.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the newly created CouchDB database.</returns>
        public async Task<CouchDatabase<TSource>> CreateDatabaseAsync<TSource>(string database) where TSource : CouchEntity
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            if (!new Regex(@"^[a-z][a-z0-9_$()+/-]*$").IsMatch(database) && !IsSystemDatabase(database))
            {
                throw new ArgumentException(nameof(database), $"Name {database} contains invalid characters. Please visit: https://docs.couchdb.org/en/stable/api/database/common.html#put--db");
            }

            await NewRequest()
                .AppendPathSegment(database)
                .PutAsync(null)
                .SendRequestAsync();

            return new CouchDatabase<TSource>(_flurlClient, _settings, ConnectionString, database);
        }

        private bool IsSystemDatabase(string database) => database == "_users" || database == "_replicator" || database == "_global_changes";

        /// <summary>
        /// Deletes the database with the given name from the server.
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <param name="database">The database name.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task DeleteDatabaseAsync<TSource>(string database) where TSource : CouchEntity
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            await NewRequest()
                .AppendPathSegment(database)
                .DeleteAsync()
                .SendRequestAsync();
        }

        #endregion

        #region CRUD reflection

        /// <summary>
        /// Returns an instance of the CouchDB database of the given type. 
        /// If EnsureDatabaseExists is configured, it creates the database if it doesn't exists.
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <returns>The instance of the CouchDB database of the given type.</returns>
        public CouchDatabase<TSource> GetDatabase<TSource>() where TSource : CouchEntity
        {
            return GetDatabase<TSource>(GetClassName<TSource>());
        }
        /// <summary>
        /// Creates a new database of the given type in the server. 
        /// The name must begin with a lowercase letter and can contains only lowercase characters, digits or _, $, (, ), +, - and /.s
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <param name="database">The database name.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the newly created CouchDB database.</returns>
        public Task<CouchDatabase<TSource>> CreateDatabaseAsync<TSource>() where TSource : CouchEntity
        {
            return CreateDatabaseAsync<TSource>(GetClassName<TSource>());
        }
        /// <summary>
        /// Deletes the database with the given type from the server.
        /// </summary>
        /// <typeparam name="TSource">The type of database documents.</typeparam>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task DeleteDatabaseAsync<TSource>() where TSource : CouchEntity
        {
            return DeleteDatabaseAsync<TSource>(GetClassName<TSource>());
        }
        private string GetClassName<TSource>()
        {
            var type = typeof(TSource);
            return type.GetName(_settings);
        }

        #endregion

        #region Utils

        /// <summary>
        /// Returns all databases names in the server.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains the sequence of databases names.</returns>
        public async Task<IEnumerable<string>> GetDatabasesNamesAsync()
        {
            return await NewRequest()
                .AppendPathSegment("_all_dbs")
                .GetJsonAsync<IEnumerable<string>>()
                .SendRequestAsync();
        }
        /// <summary>
        /// Returns all active tasks in the server.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains the sequence of all active tasks.</returns>
        public async Task<IEnumerable<CouchActiveTask>> GetActiveTasksAsync()
        {
            return await NewRequest()
                .AppendPathSegment("_active_tasks")
                .GetJsonAsync<IEnumerable<CouchActiveTask>>()
                .SendRequestAsync();
        }

        #endregion

        #endregion

        #region Implementations

        private IFlurlRequest NewRequest()
        {
            return _flurlClient.Request(ConnectionString);
        }

        /// <summary>
        /// Performs the logout and disposes the HTTP client.
        /// </summary>
        public void Dispose()
        {
            AsyncContext.Run(() => LogoutAsync());
            _flurlClient.Dispose();
        }

        #endregion
    }
}
