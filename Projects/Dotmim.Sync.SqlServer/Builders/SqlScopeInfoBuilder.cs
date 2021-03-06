﻿using Dotmim.Sync.Builders;
using Dotmim.Sync.Log;
using Dotmim.Sync.SqlServer.Manager;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;

namespace Dotmim.Sync.SqlServer.Scope
{
    public class SqlScopeInfoBuilder : IDbScopeInfoBuilder
    {
        protected readonly ParserName scopeTableName;
        protected readonly SqlConnection connection;
        protected readonly SqlTransaction transaction;

        public SqlScopeInfoBuilder(string scopeTableName, DbConnection connection, DbTransaction transaction = null)
        {
            this.connection = connection as SqlConnection;
            this.transaction = transaction as SqlTransaction;
            this.scopeTableName = ParserName.Parse(scopeTableName);
            
        }

        public virtual void CreateScopeInfoTable()
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            bool alreadyOpened = connection.State == ConnectionState.Open;

            try
            {
                if (!alreadyOpened)
                    connection.Open();

                command.CommandText =
                    $@"CREATE TABLE [dbo].{scopeTableName.Quoted().ToString()}(
                        [sync_scope_id] [uniqueidentifier] NOT NULL,
	                    [sync_scope_name] [nvarchar](100) NOT NULL,
	                    [sync_scope_schema] [nvarchar](max) NULL,
                        [scope_last_server_sync_timestamp] [bigint] NULL,
                        [scope_last_sync_timestamp] [bigint] NULL,
                        [scope_last_sync_duration] [bigint] NULL,
                        [scope_last_sync] [datetime] NULL
                        CONSTRAINT [PK_{scopeTableName.Unquoted().Normalized().ToString()}] PRIMARY KEY CLUSTERED ([sync_scope_id] ASC)
                        )";
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during CreateTableScope : {ex}");
                throw;
            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }

        public virtual void DropScopeInfoTable()
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            bool alreadyOpened = connection.State == ConnectionState.Open;

            try
            {
                if (!alreadyOpened)
                    connection.Open();

                command.CommandText =$"DROP Table [dbo].{scopeTableName.Quoted().ToString()}";

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during DropScopeInfoTable : {ex}");
                throw;
            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }

        public virtual List<ScopeInfo> GetAllScopes(string scopeName)
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;

            bool alreadyOpened = connection.State == ConnectionState.Open;

            var scopes = new List<ScopeInfo>();
            try
            {
                if (!alreadyOpened)
                    connection.Open();

                command.CommandText =
                    $@"SELECT [sync_scope_id]
                           , [sync_scope_name]
                           , [sync_scope_schema]
                           , [scope_last_sync]
                           , [scope_last_server_sync_timestamp]
                           , [scope_last_sync_timestamp]
                           , [scope_last_sync_duration]
                    FROM  {scopeTableName.Quoted().ToString()}
                    WHERE [sync_scope_name] = @sync_scope_name";

                var p = command.CreateParameter();
                p.ParameterName = "@sync_scope_name";
                p.Value = scopeName;
                p.DbType = DbType.String;
                command.Parameters.Add(p);

                using (DbDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        // read only the first one
                        while (reader.Read())
                        {
                            var scopeInfo = new ScopeInfo();
                            scopeInfo.Name = reader["sync_scope_name"] as string;
                            scopeInfo.Schema = reader["sync_scope_schema"] as string;
                            scopeInfo.Id = (Guid)reader["sync_scope_id"];
                            scopeInfo.LastSync = reader["scope_last_sync"] != DBNull.Value ? (DateTime?)reader["scope_last_sync"] : null;
                            scopeInfo.LastServerSyncTimestamp = reader["scope_last_server_sync_timestamp"] != DBNull.Value ? (long)reader["scope_last_server_sync_timestamp"] : 0;
                            scopeInfo.LastSyncTimestamp = reader["scope_last_sync_timestamp"] != DBNull.Value ? (long)reader["scope_last_sync_timestamp"] : 0;
                            scopeInfo.LastSyncDuration = reader["scope_last_sync_duration"] != DBNull.Value ? (long)reader["scope_last_sync_duration"] : 0;
                            scopes.Add(scopeInfo);
                        }
                    }
                }

                return scopes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during GetAllScopes : {ex}");
                throw;
            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }

        public virtual long GetLocalTimestamp()
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;

            bool alreadyOpened = connection.State == ConnectionState.Open;
            try
            {
                // UPDATE Nov 2019 : We don't use min_active_rowversion anymore, since we are in a transaction
                // and we still need the last row version, so check back to @@DBTS
                command.CommandText = "SELECT @sync_new_timestamp = @@DBTS";
                DbParameter p = command.CreateParameter();
                p.ParameterName = "@sync_new_timestamp";
                p.DbType = DbType.Int64;
                p.Direction = ParameterDirection.Output;
                command.Parameters.Add(p);

                if (!alreadyOpened)
                    connection.Open();

                command.ExecuteNonQuery();

                var outputParameter = SqlManager.GetParameter(command, "sync_new_timestamp");

                if (outputParameter == null)
                    return 0L;

                long result = 0L;

                long.TryParse(outputParameter.Value.ToString(), out result);

                command.Dispose();

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during GetLocalTimestamp : {ex}");
                throw;

            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }

        public virtual ScopeInfo InsertOrUpdateScopeInfo(ScopeInfo scopeInfo)
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            bool alreadyOpened = connection.State == ConnectionState.Open;

            try
            {
                if (!alreadyOpened)
                    connection.Open();

                command.CommandText = $@"
                    MERGE {scopeTableName.Quoted().ToString()} AS [base] 
                    USING (
                               SELECT  @sync_scope_id AS sync_scope_id,  
	                                   @sync_scope_name AS sync_scope_name,  
	                                   @sync_scope_schema AS sync_scope_schema,  
                                       @scope_last_sync AS scope_last_sync,
                                       @scope_last_sync_timestamp AS scope_last_sync_timestamp,
                                       @scope_last_server_sync_timestamp AS scope_last_server_sync_timestamp,
                                       @scope_last_sync_duration AS scope_last_sync_duration
                           ) AS [changes] 
                    ON [base].[sync_scope_id] = [changes].[sync_scope_id]
                    WHEN NOT MATCHED THEN
	                    INSERT ([sync_scope_name], [sync_scope_schema], [sync_scope_id], [scope_last_sync], [scope_last_sync_timestamp],           [scope_last_server_sync_timestamp],           [scope_last_sync_duration])
	                    VALUES ([changes].[sync_scope_name], [changes].[sync_scope_schema], [changes].[sync_scope_id], [changes].[scope_last_sync],  [changes].[scope_last_sync_timestamp], [changes].[scope_last_server_sync_timestamp], [changes].[scope_last_sync_duration])
                    WHEN MATCHED THEN
	                    UPDATE SET [sync_scope_name] = [changes].[sync_scope_name], 
                                   [sync_scope_schema] = [changes].[sync_scope_schema], 
                                   [scope_last_sync] = [changes].[scope_last_sync],
                                   [scope_last_sync_timestamp] = [changes].[scope_last_sync_timestamp],
                                   [scope_last_server_sync_timestamp] = [changes].[scope_last_server_sync_timestamp],
                                   [scope_last_sync_duration] = [changes].[scope_last_sync_duration]
                    OUTPUT  INSERTED.[sync_scope_name], 
                            INSERTED.[sync_scope_schema], 
                            INSERTED.[sync_scope_id], 
                            INSERTED.[scope_last_sync],
                            INSERTED.[scope_last_sync_timestamp],
                            INSERTED.[scope_last_server_sync_timestamp],
                            INSERTED.[scope_last_sync_duration];
                ";

                var p = command.CreateParameter();
                p.ParameterName = "@sync_scope_name";
                p.Value = scopeInfo.Name;
                p.DbType = DbType.String;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@sync_scope_schema";
                p.Value = string.IsNullOrEmpty(scopeInfo.Schema) ? DBNull.Value : (object)scopeInfo.Schema;
                p.DbType = DbType.String;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@sync_scope_id";
                p.Value = scopeInfo.Id;
                p.DbType = DbType.Guid;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@scope_last_sync";
                p.Value = scopeInfo.LastSync.HasValue ? (object)scopeInfo.LastSync.Value : DBNull.Value;
                p.DbType = DbType.DateTime;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@scope_last_sync_timestamp";
                p.Value = scopeInfo.LastSyncTimestamp;
                p.DbType = DbType.Int64;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@scope_last_server_sync_timestamp";
                p.Value = scopeInfo.LastServerSyncTimestamp;
                p.DbType = DbType.Int64;
                command.Parameters.Add(p);

                p = command.CreateParameter();
                p.ParameterName = "@scope_last_sync_duration";
                p.Value = scopeInfo.LastSyncDuration;
                p.DbType = DbType.Int64;
                command.Parameters.Add(p);


                using (DbDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            scopeInfo.Name = reader["sync_scope_name"] as String;
                            scopeInfo.Schema = reader["sync_scope_schema"] as String;
                            scopeInfo.Id = (Guid)reader["sync_scope_id"];
                            scopeInfo.LastSync = reader["scope_last_sync"] != DBNull.Value ? (DateTime?)reader["scope_last_sync"] : null;
                            scopeInfo.LastSyncTimestamp = reader["scope_last_sync_timestamp"] != DBNull.Value ? (long)reader["scope_last_sync_timestamp"] : 0;
                            scopeInfo.LastServerSyncTimestamp = reader["scope_last_server_sync_timestamp"] != DBNull.Value ? (long)reader["scope_last_server_sync_timestamp"] : 0;
                            scopeInfo.LastSyncDuration = reader["scope_last_sync_duration"] != DBNull.Value ? (long)reader["scope_last_sync_duration"] : 0;
                        }
                    }
                }

                return scopeInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during CreateTableScope : {ex}");
                throw;
            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }


        public virtual bool NeedToCreateScopeInfoTable()
        {
            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            bool alreadyOpened = connection.State == ConnectionState.Open;

            try
            {
                if (!alreadyOpened)
                    connection.Open();

                command.CommandText =
                    $@"IF EXISTS (SELECT t.name FROM sys.tables t 
                            JOIN sys.schemas s ON s.schema_id = t.schema_id 
                            WHERE t.name = N'{this.scopeTableName.Unquoted().ToString()}')
                     SELECT 1 
                     ELSE
                     SELECT 0";

                return (int)command.ExecuteScalar() != 1;

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during NeedToCreateScopeInfoTable command : {ex}");
                throw;
            }
            finally
            {
                if (!alreadyOpened && connection.State != ConnectionState.Closed)
                    connection.Close();

                if (command != null)
                    command.Dispose();
            }
        }

       

    }
}
