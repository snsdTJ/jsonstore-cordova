﻿/*
 *     Copyright 2016 IBM Corp.
 *     Licensed under the Apache License, Version 2.0 (the "License");
 *     you may not use this file except in compliance with the License.
 *     You may obtain a copy of the License at
 *     http://www.apache.org/licenses/LICENSE-2.0
 *     Unless required by applicable law or agreed to in writing, software
 *     distributed under the License is distributed on an "AS IS" BASIS,
 *     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *     See the License for the specific language governing permissions and
 *     limitations under the License.
 */

using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace JSONStoreWin8Lib.JSONStore
{
    class JSONStoreDBMgr
    {
        public static string DEFAULT_USERNAME = "jsonstore";
        public static string DB_PATH_EXT = ".sqlite";
        public static string DB_SUB_DIR = "wljsonstore";

        public static string FIELD_ID = "_id";
        public static string FIELD_JSON = "json";
        public static string FIELD_COUNT = "count(*)";
        public static string FIELD_SQL = "sql";
        public static string FIELD_OPERATION = "_operation";
        public static string FIELD_DIRTY = "_dirty";
        public static string FIELD_DELETED = "_deleted";

        public string lastErrorMsg { get; set; }
        public string dbDirectoryPath { get; set; }

        private readonly string _username;
        private SQLiteConnection _connection;

        public JSONStoreDBMgr(string username)
        {
            _username = username;
            initialize();
        }

        private void initialize()
        {
            try
            {
                var dbFileTask = getDBFilePath();
                dbFileTask.Wait();
                _connection = new SQLiteConnection(dbFileTask.Result);
            }
            catch (Exception e)
            {
                lastErrorMsg = e.ToString();
            }
        }

        private async Task<string> getDBFilePath()
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFolder dbDirectory = await localFolder.CreateFolderAsync(DB_SUB_DIR, CreationCollisionOption.OpenIfExists);

            dbDirectoryPath = dbDirectory.Path;
            
            string dbPath;
            if (_username != null)
            {
                dbPath = _username + DB_PATH_EXT;
            }
            else
            {
                dbPath = DEFAULT_USERNAME + DB_PATH_EXT;
            }

            string databasePath = Path.Combine(dbDirectoryPath, dbPath);
            return databasePath;
        }

        public void destroyDBDirectory(Boolean destroyAll)
        {
            _connection.Dispose();
            _connection = null;
            GC.Collect();
            var dbDestroyTask = destroyAllDBFiles(dbDirectoryPath, DB_SUB_DIR, destroyAll, _username);
            dbDestroyTask.Wait();
        }

        private static async Task destroyAllDBFiles(string dbDirPath, string dirName, Boolean destroyAll, string username)
        {
            var dbDir = await ApplicationData.Current.LocalFolder.GetFolderAsync(dirName);
            if (dbDir.Path == dbDirPath)
            {
                StorageFolder dbDirectory = await StorageFolder.GetFolderFromPathAsync(dbDirPath);
                if(destroyAll) {
                    IEnumerable<IStorageFile> files = await dbDirectory.GetFilesAsync();
                    foreach (IStorageFile anyfile in files)
                    {
                        try
                        {
                            await anyfile.DeleteAsync();
                        }
                        catch (Exception)
                        {
                            //log error message
                        }
                    }    
                } else {
                    IStorageFile file = await dbDirectory.GetFileAsync(username + DB_PATH_EXT);
                    try {
                        await file.DeleteAsync();
                    } catch (Exception) {

                    }
                }
                
            }
        }

        public bool execute(string statement, params object[] args)
        {
            if (_connection != null) {
                try {
                    _connection.Execute(statement, args);
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        public int executeRowsModified(string statement, params object[] args)
        {
            int result = 0;
            if (_connection != null)
            {
                try
                {
                    result = _connection.Execute(statement, args);
                    return result;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return result;
        }

        public bool selectInto(out IDictionary<string, string> results, string selectStmt) 
        {
            results = new Dictionary<string, string>();
            if (_connection != null)
            {
                try
                {
                    var selectResults = _connection.Query<SelectResult>(selectStmt);
                    
                    if (selectResults.Count > 0) 
                    {
                        SelectResult result = selectResults[0];
                        
                        results.Add(FIELD_SQL, result.sql);
                        results.Add(FIELD_COUNT, result.count);
                        results.Add(FIELD_DIRTY, result.dirty.ToString());
                        results.Add(FIELD_OPERATION, result.operation);
                        results.Add(FIELD_DELETED, result.deleted.ToString());
                    }
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        public bool selectAllInto(JArray results, string query)
        {
            if (_connection != null)
            {
                try
                {
                    var cmd = _connection.CreateCommand(query);
                    var stmt = SQLite3.Prepare2(_connection.Handle, cmd.CommandText);

                    try
                    {
                        while (SQLite3.Step(stmt) == SQLite3.Result.Row)
                        {
                            int colCount = SQLite3.ColumnCount(stmt);
                            if (colCount <= 0)
                            {
                                return false;
                            }

                            JObject jsonObj = new JObject();

                            for (int i = 0; i < colCount; i++)
                            {
                                string columnName = SQLite3.ColumnName16(stmt, i);

                                if (columnName.Equals(FIELD_JSON))
                                {
                                    MemoryStream ms = new MemoryStream(SQLite3.ColumnByteArray(stmt, i));
                                    using (BsonReader reader = new BsonReader(ms))
                                    {
                                        jsonObj.Add(FIELD_JSON, JToken.ReadFrom(reader));
                                    }
                                }
                                else if (columnName.Equals(FIELD_ID))
                                {
                                    jsonObj.Add(FIELD_ID, Int32.Parse(SQLite3.ColumnString(stmt, i)));
                                }
                                else
                                {
                                    if (isJSONCreatedColumn(columnName))
                                    {
                                        jsonObj.Add(columnName, SQLite3.ColumnString(stmt, i));
                                    }
                                    else
                                    {
                                        jsonObj.Add(columnName.Replace('_', '.'), SQLite3.ColumnString(stmt, i));
                                    }
                                   
                                }
                            }
                            results.Add(jsonObj);
                        }
                    }
                    finally
                    {
                        SQLite3.Finalize(stmt);
                    }
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        private bool isJSONCreatedColumn(string column)
        {
            if (column.Equals(FIELD_DELETED) || column.Equals(FIELD_DIRTY) ||
                column.Equals(FIELD_ID) || column.Equals(FIELD_JSON) ||
                column.Equals(FIELD_OPERATION))
                return true;
         
            return false;
        }
        
        public bool startTransaction()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.BeginTransaction();
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        public bool commitTransaction()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.Commit();
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        public bool rollbackTransaction()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.Rollback();
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

        public bool close()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.Close();
                    return true;
                }
                catch (Exception e)
                {
                    lastErrorMsg = e.ToString();
                }
            }
            return false;
        }

    }

    class SelectResult
    {
        public string sql { get; set; }
        
        [ColumnAttribute("_operation")]
        public string operation { get; set; }
        
        [ColumnAttribute("count(*)")]
        public string count { get; set; }

        [ColumnAttribute("_dirty")]
        public DateTimeOffset dirty { get; set; }

        [ColumnAttribute("_deleted")]
        public int deleted { get; set; }
    }

}
