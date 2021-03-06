﻿using Dotmim.Sync.Batch;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Messages;
using Dotmim.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{

    /// <summary>
    /// Sync agent. It's the sync orchestrator
    /// Knows both the Sync Server provider and the Sync Client provider
    /// </summary>
    public class SyncAgent : IDisposable
    {
        private IProgress<ProgressArgs> remoteProgress = null;
        private bool syncInProgress;

        /// <summary>
        /// Defines the state that a synchronization session is in.
        /// </summary>
        public SyncSessionState SessionState { get; set; }

        /// <summary>
        /// Gets or Sets the local orchestrator
        /// </summary>
        public ILocalOrchestrator LocalOrchestrator { get; set; }

        /// <summary>
        /// Get or Sets the remote orchestrator
        /// </summary>
        public IRemoteOrchestrator RemoteOrchestrator { get; set; }

        /// <summary>
        /// Get or Sets the Sync parameters to pass to Remote provider for filtering rows
        /// </summary>
        public SyncParameters Parameters { get; private set; }

        /// <summary>
        /// Occurs when sync is starting, ending
        /// </summary>
        public event EventHandler<SyncSessionState> SessionStateChanged = null;

        /// <summary>
        /// Gets or Sets the schema used for this sync process.
        /// </summary>
        public SyncSet Schema { get; private set; }

        /// <summary>
        /// Gets or Sets the setup used for this sync
        /// </summary>
        public SyncSetup Setup { get; set; }

        /// <summary>
        /// Gets or Sets the options used on this sync process.
        /// </summary>
        public SyncOptions Options { get; set; }

        /// <summary>
        /// Set interceptors on the LocalOrchestrator
        /// </summary>
        public void SetInterceptors(Interceptors interceptors)
            => this.LocalOrchestrator.On(interceptors);

        /// <summary>
        /// If you want to see remote progress as well (only available RemoteOrchestrator)
        /// </summary>
        /// <param name="remoteProgress"></param>
        public void AddRemoteProgress(IProgress<ProgressArgs> remoteProgress) => this.remoteProgress = remoteProgress;

        /// <summary>
        /// Shortcut to Apply changed failed if remote orchestrator supports it
        /// </summary>
        public void OnApplyChangesFailed(Func<ApplyChangesFailedArgs, Task> func)
            => this.RemoteOrchestrator.OnApplyChangesFailed(func);

        /// <summary>
        /// Shortcut to Apply changed failed if remote orchestrator supports it
        /// </summary>
        public void OnApplyChangesFailed(Action<ApplyChangesFailedArgs> action)
            => this.RemoteOrchestrator.OnApplyChangesFailed(action);


        /// <summary>
        /// Lock sync to prevent multi call to sync at the same time
        /// </summary>
        private void LockSync()
        {
            lock (this)
            {
                if (this.syncInProgress)
                    throw new AlreadyInProgressException();

                this.syncInProgress = true;
            }
        }

        /// <summary>
        /// Unlock sync to be able to launch a new sync
        /// </summary>
        private void UnlockSync()
        {
            // Enf sync from local provider
            lock (this)
            {
                this.syncInProgress = false;
            }
        }
        /// <summary>
        /// Create an agent based on TCP connection
        /// </summary>
        /// <param name="clientProvider">local provider to your client database</param>
        /// <param name="serverProvider">local provider to your server database</param>
        /// <param name="tables">tables list</param>
        public SyncAgent(CoreProvider clientProvider, CoreProvider serverProvider, string[] tables)
            : this(new LocalOrchestrator(clientProvider), new RemoteOrchestrator(serverProvider), new SyncSetup(tables))
        {
        }


        /// <summary>
        /// Create an agent based on TCP connection
        /// </summary>
        /// <param name="clientProvider">local provider to your client database</param>
        /// <param name="serverProvider">local provider to your server database</param>
        /// <param name="setup">Contains list of your tables.</param>
        public SyncAgent(CoreProvider clientProvider, CoreProvider serverProvider, SyncSetup setup)
            : this(new LocalOrchestrator(clientProvider), new RemoteOrchestrator(serverProvider), setup)
        {
        }


        /// <summary>
        /// Create an agent based on orchestrators. The remote orchestrator could be of type RemoteOrchestrator (TCP connection) our WebClientOrchestrator (Http connection)
        /// </summary>
        /// <param name="localOrchestrator">local orchestrator using a local provider</param>
        /// <param name="remoteOrchestrator">remote orchestrator : RemoteOrchestrator or WebClientOrchestrator) </param>
        /// <param name="tables">tables list</param>
        public SyncAgent(LocalOrchestrator localOrchestrator, IRemoteOrchestrator remoteOrchestrator, string[] tables)
            : this(localOrchestrator, remoteOrchestrator, new SyncSetup(tables))

        {

        }

        /// <summary>
        /// Create an agent where the remote orchestrator could be of type RemoteOrchestrator (TCP connection) or WebClientOrchestrator (Http connection)
        /// </summary>
        /// <param name="clientProvider">local provider to your client database</param>
        /// <param name="remoteOrchestrator">remote orchestrator : RemoteOrchestrator or WebClientOrchestrator) </param>
        /// <param name="setup">Contains list of your tables. Not used if remote orchestrator is WebClientOrchestrator</param>
        /// <param name="options">Options. Only used on locally if remote orchestrator is WebClientOrchestrator</param>
        public SyncAgent(CoreProvider clientProvider, IRemoteOrchestrator remoteOrchestrator,
                         SyncSetup setup = null, SyncOptions options = null)
            : this(new LocalOrchestrator(clientProvider), remoteOrchestrator, setup, options)
        {
        }


        /// <summary>
        /// Create an agent based on orchestrators. The remote orchestrator could be of type RemoteOrchestrator (TCP connection) our WebClientOrchestrator (Http connection)
        /// </summary>
        /// <param name="localOrchestrator">local orchestrator using a local provider</param>
        /// <param name="remoteOrchestrator">remote orchestrator : RemoteOrchestrator or WebClientOrchestrator) </param>
        /// <param name="setup">Contains list of your tables. Not used if remote orchestrator is WebClientOrchestrator</param>
        /// <param name="options">Options. Only used on locally if remote orchestrator is WebClientOrchestrator</param>
        public SyncAgent(LocalOrchestrator localOrchestrator, IRemoteOrchestrator remoteOrchestrator,
                         SyncSetup setup = null, SyncOptions options = null)
        {
            if (remoteOrchestrator.Provider != null && !remoteOrchestrator.Provider.CanBeServerProvider)
                throw new NotSupportedException();

            // tables to add
            this.Setup = setup ?? new SyncSetup();

            // Create sync options if needed
            this.Options = options ?? new SyncOptions();

            // Add parameters
            this.Parameters = new SyncParameters();

            // Affect local and remote orchestrators
            this.LocalOrchestrator = localOrchestrator;
            this.RemoteOrchestrator = remoteOrchestrator;
        }

        /// <summary>
        /// Launch a normal synchronization without any IProgess or CancellationToken
        /// </summary>
        public Task<SyncContext> SynchronizeAsync() => SynchronizeAsync(SyncType.Normal, CancellationToken.None);

        /// <summary>
        /// Launch a normal synchronization without any IProgess or CancellationToken
        /// </summary>
        public Task<SyncContext> SynchronizeAsync(IProgress<ProgressArgs> progress) => SynchronizeAsync(SyncType.Normal, CancellationToken.None, progress);

        /// <summary>
        /// Launch a synchronization with a SyncType specified
        /// </summary>
        public Task<SyncContext> SynchronizeAsync(SyncType syncType, IProgress<ProgressArgs> progress = null) => SynchronizeAsync(syncType, CancellationToken.None, progress);

        /// <summary>
        /// Launch a synchronization with the specified mode
        /// </summary>
        public async Task<SyncContext> SynchronizeAsync(SyncType syncType, CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {
            // for view purpose, if needed
            if (this.LocalOrchestrator?.Provider != null)
                this.LocalOrchestrator.Provider.Options = this.Options;

            if (this.RemoteOrchestrator?.Provider != null)
                this.RemoteOrchestrator.Provider.Options = this.Options;

            var jsonConverter = new JsonConverter<SyncSet>();

            // Lock sync to prevent multi call to sync at the same time
            LockSync();

            // Context, used to back and forth data between servers
            var context = new SyncContext(Guid.NewGuid())
            {
                // set start time
                StartTime = DateTime.UtcNow,
                // if any parameters, set in context
                Parameters = this.Parameters,
                // set sync type (Normal, Reinitialize, ReinitializeWithUpload)
                SyncType = syncType
            };

            this.SessionState = SyncSessionState.Synchronizing;
            this.SessionStateChanged?.Invoke(this, this.SessionState);

            try
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                ScopeInfo scope = null;

                // Starts sync by :
                // - Getting local config we have set by code
                // - Ensure local scope is created (table and values)
                (context, scope) = await this.LocalOrchestrator.EnsureScopeAsync
                        (context, this.Setup.ScopeName, this.Options.ScopeInfoTableName, cancellationToken, progress);

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // check if we already have a schema from local 
                if (!string.IsNullOrEmpty(scope.Schema))
                {
                        this.Schema = Newtonsoft.Json.JsonConvert.DeserializeObject<SyncSet>(scope.Schema);
                    //this.Schema = JsonSerializer.Deserialize<SyncSet>(scope.Schema, new JsonSerializerOptions { IgnoreReadOnlyProperties = true, IgnoreNullValues = true, MaxDepth = int.MaxValue });
                }
                else
                {
                    // FIRST call to server
                    // Get the server scope info and server reference id to local scope
                    // Be sure options / schema from client are passed if needed
                    // Then the configuration with full schema
                    var serverSchema = await this.RemoteOrchestrator.EnsureSchemaAsync(
                            context, this.Setup, cancellationToken, remoteProgress);

                    context = serverSchema.context;
                    this.Schema = serverSchema.schema;
                }


                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // on local orchestrator, get local changes
                // Most probably the schema has changed, so we passed it again (coming from Server)
                // Don't need to pass again Options since we are not modifying it between server and client
                var clientChanges = await this.LocalOrchestrator.GetChangesAsync(
                    context, this.Schema, scope, this.Options.BatchSize, this.Options.BatchDirectory, cancellationToken, progress);

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // set context
                context = clientChanges.context;

                // Get if we need to get all rows from the datasource
                var fromScratch = scope.IsNewScope || context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload;

                // IF is new and we have a snapshot directory, try to apply a snapshot
                if (fromScratch)
                {
                    // Get snapshot files
                    var serverSnapshotChanges = await this.RemoteOrchestrator.GetSnapshotAsync(
                        context, scope, this.Schema, this.Options.SnapshotsDirectory, this.Options.BatchDirectory, cancellationToken, remoteProgress);

                    if (serverSnapshotChanges.serverBatchInfo != null)
                    {
                        var hasChanges = await serverSnapshotChanges.serverBatchInfo.HasDataAsync();
                        if (hasChanges)
                        {
                            context = await this.LocalOrchestrator.ApplySnapshotAndGetChangesAsync(context, scope, this.Schema, serverSnapshotChanges.serverBatchInfo,
                                clientChanges.clientTimestamp, serverSnapshotChanges.remoteClientTimestamp, this.Options.DisableConstraintsOnApplyChanges,
                                this.Options.BatchSize, this.Options.BatchDirectory, this.Options.UseBulkOperations, this.Options.CleanMetadatas, this.Options.ScopeInfoTableName,
                                cancellationToken, progress);

                        }
                    }
                }

                var serverChanges = await this.RemoteOrchestrator.ApplyThenGetChangesAsync(
                    context, scope, this.Schema, clientChanges.clientBatchInfo, this.Options.DisableConstraintsOnApplyChanges, this.Options.UseBulkOperations,
                    this.Options.CleanMetadatas, this.Options.CleanFolder, this.Options.BatchSize, this.Options.BatchDirectory,
                    this.Options.ConflictResolutionPolicy, cancellationToken, remoteProgress);

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // set context
                context = serverChanges.context;
                // Serialize schema to be able to save it in client db
                if (string.IsNullOrEmpty(scope.Schema))
                {
                    var schemaLight = Newtonsoft.Json.JsonConvert.SerializeObject(this.Schema);
                    scope.Schema = schemaLight;
                }

                // Policy is always Server policy, so reverse this policy to get the client policy
                var clientPolicy = this.Options.ConflictResolutionPolicy == ConflictResolutionPolicy.ServerWins ? ConflictResolutionPolicy.ClientWins : ConflictResolutionPolicy.ServerWins;

                var localChanges = await this.LocalOrchestrator.ApplyChangesAsync(
                    context, scope, this.Schema, serverChanges.serverBatchInfo,
                    clientPolicy, clientChanges.clientTimestamp, serverChanges.remoteClientTimestamp,
                    this.Options.DisableConstraintsOnApplyChanges, this.Options.UseBulkOperations,
                    this.Options.CleanMetadatas, this.Options.ScopeInfoTableName, true,
                    cancellationToken, progress);

                context.TotalChangesDownloaded += localChanges.clientChangesApplied.TotalAppliedChanges;
                context.TotalChangesUploaded += clientChanges.clientChangesSelected.TotalChangesSelected;
                context.TotalSyncErrors += localChanges.clientChangesApplied.TotalAppliedChangesFailed;


                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

            }
            catch (SyncException se)
            {
                Debug.WriteLine($"Sync Exception: {se.Message}. TypeName:{se.TypeName}.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unknwon Exception: {ex.Message}.");
                throw new SyncException(ex, SyncStage.None);
            }
            finally
            {
                // End the current session
                this.SessionState = SyncSessionState.Ready;
                this.SessionStateChanged?.Invoke(this, this.SessionState);
                // unlock sync since it's over
                UnlockSync();
            }

            return context;
        }

        // --------------------------------------------------------------------
        // Dispose
        // --------------------------------------------------------------------

        /// <summary>
        /// Releases all resources used by the <see cref="T:Microsoft.Synchronization.Data.DbSyncBatchInfo" />.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used 
        /// </summary>
        protected virtual void Dispose(bool cleanup)
        {

        }
    }
}
