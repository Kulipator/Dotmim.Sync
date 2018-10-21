using Dotmim.Sync.Builders;
using Dotmim.Sync.Data;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Filter;
using Dotmim.Sync.Messages;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Core provider : should be implemented by any server / client provider
    /// </summary>
    public abstract partial class CoreProvider
    {
        /// <summary>
        /// Deprovision a database. You have to passe a configuration object, containing at least the dmTables
        /// </summary>
        public async Task DeprovisionAsync(SyncConfiguration configuration, SyncProvision provision)
        {
            DbConnection connection = null;
            try
            {
                if (configuration.Schema == null || !configuration.Schema.HasTables)
                    throw new ArgumentNullException("tables", "You must set the tables you want to provision");

                // Load the configuration
                await this.ReadSchemaAsync(configuration.Schema);

                // Open the connection
                using (connection = this.CreateConnection())
                {
                    await connection.OpenAsync();

                    using (var transaction = connection.BeginTransaction())
                    {
                        for (var i = configuration.Count - 1; i >= 0; i--)
                        {
                            // Get the table
                            var dmTable = configuration.Schema.Tables[i];

                            // get the builder
                            var builder = this.GetDatabaseBuilder(dmTable);

                            // adding filters
                            this.AddFilters(configuration.Filters, dmTable, builder);

                            if (provision.HasFlag(SyncProvision.TrackingTable) || provision.HasFlag(SyncProvision.All))
                                builder.DropTrackingTable(connection, transaction);

                            if (provision.HasFlag(SyncProvision.StoredProcedures) || provision.HasFlag(SyncProvision.All))
                                builder.DropProcedures(connection, transaction);

                            if (provision.HasFlag(SyncProvision.Triggers) || provision.HasFlag(SyncProvision.All))
                                builder.DropTriggers(connection, transaction);

                            // On purpose, the flag SyncProvision.All does not include the SyncProvision.Table, too dangerous...
                            if (provision.HasFlag(SyncProvision.Table))
                                builder.DropTable(connection, transaction);
                        }

                        if (provision.HasFlag(SyncProvision.Scope) || provision.HasFlag(SyncProvision.All))
                        {
                            var scopeBuilder = this.GetScopeBuilder().CreateScopeInfoBuilder(configuration.ScopeInfoTableName, connection, transaction);
                            if (!scopeBuilder.NeedToCreateScopeInfoTable())
                                scopeBuilder.DropScopeInfoTable();
                        }
                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new SyncException(ex, SyncStage.DatabaseApplying, this.ProviderTypeName);
            }
            finally
            {
                if (connection != null && connection.State != ConnectionState.Closed)
                    connection.Close();
            }

        }

        /// <summary>
        /// Deprovision a database
        /// </summary>
        public async Task ProvisionAsync(SyncConfiguration configuration, SyncProvision provision)
        {
            DbConnection connection = null;

            try
            {
                if (configuration.Schema == null || !configuration.Schema.HasTables)
                    throw new ArgumentNullException("tables", "You must set the tables you want to provision");

                // Load the configuration
                await this.ReadSchemaAsync(configuration.Schema);

                // Open the connection
                using (connection = this.CreateConnection())
                {
                    await connection.OpenAsync();

                    using (var transaction = connection.BeginTransaction())
                    {

                        if (provision.HasFlag(SyncProvision.Scope) || provision.HasFlag(SyncProvision.All))
                        {
                            var scopeBuilder = this.GetScopeBuilder().CreateScopeInfoBuilder(configuration.ScopeInfoTableName, connection, transaction);
                            if (scopeBuilder.NeedToCreateScopeInfoTable())
                                scopeBuilder.CreateScopeInfoTable();
                        }

                        for (var i = 0; i < configuration.Count; i++)
                        {
                            // Get the table
                            var dmTable = configuration.Schema.Tables[i];

                            // get the builder
                            var builder = this.GetDatabaseBuilder(dmTable);

                            // adding filters
                            this.AddFilters(configuration.Filters, dmTable, builder);

                            // On purpose, the flag SyncProvision.All does not include the SyncProvision.Table, too dangerous...
                            if (provision.HasFlag(SyncProvision.Table))
                                builder.CreateTable(connection, transaction);

                            if (provision.HasFlag(SyncProvision.TrackingTable) || provision.HasFlag(SyncProvision.All))
                                builder.CreateTrackingTable(connection, transaction);

                            if (provision.HasFlag(SyncProvision.Triggers) || provision.HasFlag(SyncProvision.All))
                                builder.CreateTriggers(connection, transaction);

                            if (provision.HasFlag(SyncProvision.StoredProcedures) || provision.HasFlag(SyncProvision.All))
                                builder.CreateStoredProcedures(connection, transaction);

                        }
                        transaction.Commit();
                    }

                    connection.Close();
                }

            }
            catch (Exception ex)
            {
                throw new SyncException(ex, SyncStage.DatabaseApplying, this.ProviderTypeName);
            }
            finally
            {
                if (connection != null && connection.State != ConnectionState.Closed)
                    connection.Close();
            }
        }

        /// <summary>
        /// Be sure all tables are ready and configured for sync
        /// the ScopeSet Configuration MUST be filled by the schema form Database
        /// </summary>
        public virtual async Task<SyncContext> EnsureDatabaseAsync(SyncContext context, MessageEnsureDatabase message)
        {
            DbConnection connection = null;
            try
            {
                // Event progress
                context.SyncStage = SyncStage.DatabaseApplying;

                var beforeArgs =
                    new DatabaseApplyingEventArgs(this.ProviderTypeName, context.SyncStage, message.Schema);
                this.TryRaiseProgressEvent(beforeArgs, DatabaseApplying);

                // If scope exists and lastdatetime sync is present, so database exists
                // Check if we don't have an OverwriteConfiguration (if true, we force the check)

                if (message.ScopeInfo.LastSync.HasValue && !beforeArgs.OverwriteSchema)
                    return context;

                var script = new StringBuilder();

                // Open the connection
                using (connection = this.CreateConnection())
                {
                    await connection.OpenAsync();

                    using (var transaction = connection.BeginTransaction())
                    {
                        // Sorting tables based on dependencies between them
                        var dmTables = message.Schema.Tables;

                        //.SortByDependencies(tab => tab.ChildRelations
                        //    .Select(r => r.ChildTable));

                        foreach (var dmTable in dmTables)
                        {
                            var builder = this.GetDatabaseBuilder(dmTable);

                            // adding filter
                            this.AddFilters(message.Filters, dmTable, builder);

                            context.SyncStage = SyncStage.DatabaseTableApplying;
                            var beforeTableArgs =
                                new DatabaseTableApplyingEventArgs(this.ProviderTypeName, context.SyncStage, dmTable.TableName);
                            this.TryRaiseProgressEvent(beforeTableArgs, DatabaseTableApplying);

                            string currentScript = null;
                            if (beforeArgs.GenerateScript)
                            {
                                currentScript = builder.ScriptTable(connection, transaction);
                                currentScript += builder.ScriptForeignKeys(connection, transaction);
                                script.Append(currentScript);
                            }

                            builder.Create(connection, transaction);
                            builder.CreateForeignKeys(connection, transaction);

                            context.SyncStage = SyncStage.DatabaseTableApplied;
                            var afterTableArgs =
                                new DatabaseTableAppliedEventArgs(this.ProviderTypeName, context.SyncStage, dmTable.TableName, currentScript);
                            this.TryRaiseProgressEvent(afterTableArgs, DatabaseTableApplied);
                        }

                        context.SyncStage = SyncStage.DatabaseApplied;
                        var afterArgs = new DatabaseAppliedEventArgs(this.ProviderTypeName, context.SyncStage, script.ToString());
                        this.TryRaiseProgressEvent(afterArgs, DatabaseApplied);

                        transaction.Commit();
                    }

                    connection.Close();

                    return context;
                }

            }
            catch (Exception ex)
            {
                throw new SyncException(ex, SyncStage.DatabaseApplying, this.ProviderTypeName);
            }
            finally
            {
                if (connection != null && connection.State != ConnectionState.Closed)
                    connection.Close();
            }
        }



        /// <summary>
        /// Adding filters to an existing configuration
        /// </summary>
        private void AddFilters(List<FilterClause2> filters, DmTable dmTable, DbBuilder builder)
        {
            if (filters != null && filters.Count > 0)
            {
                foreach (var filter in filters)
                {
                    var tableFilter = builder.TableDescription.DmSet.Tables[filter.FilterTable.TableName.ObjectNameNormalized];

                    var hierarchy = dmTable.GetParentsTo(tableFilter);

                    if (hierarchy.Count > 0 || dmTable.TableName.ToLowerInvariant() == filter.FilterTable.TableName.ObjectNameNormalized.ToLowerInvariant())
                    {
                        builder.Filters.Add(filter);
                    }

                }

            }

        }
    }
}
