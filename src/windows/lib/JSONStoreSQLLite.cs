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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JSONStoreWin8Lib.JSONStore
{
    class JSONStoreSQLLite
    {

        public string username { get; set; }
        
        private static JSONStoreSQLLite sharedManagerSingleton;
        private JSONStoreDBMgr dbMgr;
        private JSONStoreIndexer indexer;
        private IDictionary<string, JSONStoreSchema> jsonSchemas;
        private bool dbHasBeenKeyed;

        private static string[] DATABASE_SPECIAL_CHARACTERS = new string[]{"@", "$", "^", "&", "|", ">", "<", "?", "-"};

        //Use this to get a singleton instance
        public static JSONStoreSQLLite sharedManager()
        {
            if (sharedManagerSingleton == null || sharedManagerSingleton.username == null) {
                return null;
        }
            return sharedManagerSingleton;
        }

        //This MUST be called first to set the username,
        //otherwise you will get an exception  from sharedManager
        public static JSONStoreSQLLite sharedManager(string username)
        {
            if (sharedManagerSingleton == null) {
                sharedManagerSingleton = new JSONStoreSQLLite(username);
            }
            
            if(!username.Equals(sharedManagerSingleton.username)) {
                return null;
            }

            return sharedManagerSingleton;
        }

        private JSONStoreSQLLite(string username)
        {
            this.username = username;
            this.jsonSchemas = new Dictionary<string, JSONStoreSchema>();
            this.indexer = new JSONStoreIndexer();
        }

        public bool dropTable(string collectionName)
        {
            // If the database has been closed, re-open it.  We do this because provision could be called
            // after a close and indicate the collection needs to be dropped, so we need to open the
            // collection here.  Note that the API in StoragePlugin does NOT support dropping a closed table
            // This is only supported via provision.
            if (dbMgr == null) {
                dbMgr = new JSONStoreDBMgr(username);
            }
    
            string dropStmt = "drop table if exists '" + collectionName + "'";
            //DLog(@"Dropping table with statement: %@", dropStmt);
            return dbMgr.execute(dropStmt);
        }

        public int provisionCollection(string collectionName, JSONStoreSchema schema)
        {
            int rc = 0;

            // create the dbMgr, if needed
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }

            // store the new schema for future use
            if(jsonSchemas.ContainsKey(collectionName)) {
                jsonSchemas.Remove(collectionName);
            }
            jsonSchemas.Add(collectionName, schema);

            // create the query
            string createPref = String.Format("create table '{0}' ( _id INTEGER primary key autoincrement, ", collectionName);
            string createSuf = "json BLOB, _dirty REAL default 0, _deleted INTEGER default 0, _operation TEXT)";
            string indexedColumns = schemaFromDict(schema.getCombinedDictionary());
            string stmt;

            // combine the parts in a single statement
            if (!String.IsNullOrEmpty(indexedColumns))
            {
                stmt = createPref + indexedColumns + ", " + createSuf;
            }
            else
            {
                stmt = createPref + createSuf;
            }   
    
            if (!dbMgr.execute(stmt)) 
            {

                // If the creation indicates a failure, determine if it is due to the table already existing, which isn't really an error.
                string failedMessage = dbMgr.lastErrorMsg;
                string TABLE_EXISTS_STRING = String.Format("table '{0}' already exists", collectionName);

                if (!failedMessage.Contains(TABLE_EXISTS_STRING)) {

                    if (failedMessage.Contains(JSONStoreConstants.JSON_STORE_FILE_ENCRYPTED)) 
                    {
                        //This happens when, we weren't passed in a password, but the datatbase was encrypted, so we
                        //don't know until we try to provision.
                        rc = JSONStoreConstants.JSON_STORE_PROVISION_KEY_FAILURE;
                    } 
                    else {
                        rc = JSONStoreConstants.JSON_STORE_PROVISION_TABLE_FAILURE;
                    }

                } else {

                    if (validateExistingSchemaAgainst(indexedColumns, collectionName, createPref, createSuf)) 
                    {
                        rc = JSONStoreConstants.JSON_STORE_PROVISION_TABLE_EXISTS;

                    } 
                    else 
                    {
                        rc = JSONStoreConstants.JSON_STORE_PROVISION_TABLE_SCHEMA_MISMATCH;
                    }
                }
            }
            return rc;
        }

        public bool storeObject(JObject jsonObj, string collectionName, bool isAdd, IDictionary<string, string> additionalIndexes)
        {
            bool worked = true;
    
            // get the schema (search fields and additional search fields)
            JSONStoreSchema jsonSchema = jsonSchemas[collectionName];
    
            // creates a mapping of search fields to their values
            IDictionary<string, HashSet<string>> indexesAndValues = indexer.findIndexesFromSchema(jsonSchema, jsonObj);
    
            if (additionalIndexes != null) {

                foreach(KeyValuePair<string, string> entry in additionalIndexes)
                {
                    HashSet<string> existingValues = null;
                    if(indexesAndValues.ContainsKey(entry.Key)) {
                        existingValues = indexesAndValues[entry.Key];
                    }
            
                    if (existingValues == null) {
                        existingValues = new HashSet<string>();
                    }
                    existingValues.Add(entry.Value);

                    indexesAndValues.Add(entry.Key, existingValues);
                }
            }
    
            int rc = store(jsonObj, collectionName, indexesAndValues, isAdd);
    
            if (rc < 0) {
                worked = false;
            }
    
            return worked;
        }

        public int store(JObject jsonObj, string collectionName, IDictionary<string, HashSet<string>> indexes, bool isAdd)
        {
            int rc = 0;
            string fieldsStr = null;
    
           //Note, these are associative arrays, they need to stay in sync, we don't use a hash because order matters
           //and there are nice tricks we can do with arrays to build our statements
           List<string> fieldNames = new List<string>();
           List<object> fieldValues = new List<object>();

           foreach (KeyValuePair<string, HashSet<string>> entry in indexes)
           {

                fieldNames.Add(String.Format("'{0}'", getDatabaseSafeSearchFieldName(entry.Key)));
                string valString = "";
                foreach (string val in entry.Value)
                {
                    if (String.IsNullOrEmpty(valString))
                    {
                        valString = val;
                    }
                    else
                    {
                        valString = valString + "-@-" + val;
                    }
                }
                fieldValues.Add(valString);
            };

            fieldNames.Add(JSONStoreConstants.JSON_STORE_FIELD_JSON);
            MemoryStream ms = new MemoryStream();
            using (BsonWriter writer = new BsonWriter(ms)) {
                jsonObj.WriteTo(writer);
            }
            fieldValues.Add(ms.ToArray());
            //Store operations should not set the dirty flag, add operations should
            if (isAdd) {
                fieldNames.Add(JSONStoreConstants.JSON_STORE_FIELD_DIRTY);
                fieldValues.Add(DateTimeOffset.Now);
                fieldNames.Add(JSONStoreConstants.JSON_STORE_FIELD_OPERATION);
                fieldValues.Add(JSONStoreConstants.JSON_STORE_OP_ADD);

            } else {
                fieldNames.Add(JSONStoreConstants.JSON_STORE_FIELD_OPERATION);
                fieldValues.Add(JSONStoreConstants.JSON_STORE_OP_STORE);
            }
    
            foreach(string name in fieldNames) {
                if(fieldsStr == null) {
                    fieldsStr = name;
                } else {
                    fieldsStr = fieldsStr + "," + name;
                }
            }
    
            string valuesStr = buildValueStr(fieldValues.Count);
            string[] statementArray = new string[3];
            statementArray[0] = collectionName;
            statementArray[1] = fieldsStr;
            statementArray[2] = valuesStr;

    
            string insertStmt = String.Format("insert into '{0}' ({1}) values ({2})", statementArray);

            //DLog(@"Insert with--> %@", insertStmt);
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
    
            bool worked = dbMgr.execute(insertStmt, fieldValues.ToArray());
    
            if (! worked) {
               // DLog(@"*** STORE FAILED");
                rc =-1;
            } else {
               // DLog(@"Store succeeded");
                rc = 0;
            }
            return rc;
        }

        public JArray find(JToken query, string collection, JSONStoreQueryOptions options)
        {
            string selectStmt = selectStatement(options.filter);

            string limitAndOffsetClause = limitAndOffsetClauseWithLimit(options.hasLimit, options.limit, options.offset);

            string orderByClauseStmt = "";
            string whereClauseNotDeletedStmt = "";
    
            if (limitAndOffsetClause == null) 
            {
        
                //Negative limit edge case
                orderByClauseStmt = "ORDER BY _id DESC ";
                if (Math.Ceiling((double)Math.Abs(options.offset)) > 0) 
                {
                    limitAndOffsetClause = buildLimitAndOffsetClauseWithLimit(options.limit, options.offset);
            
                } else {
            
                    limitAndOffsetClause = buildLimitClauseWithLimit(options.limit);
                }
        
            } else {
        
                orderByClauseStmt = orderByClause(options.sort);
            }
    
           // if (query.isKindOfClass(NSDictionary)) {
        
                whereClauseNotDeletedStmt = whereClauseNotDeleted(query, " AND ", options.exact);
    
           // }
           // else {
        
                //Fail, query can only be a query (dictionary) or array of ids
              //  return null;
          //  }
    
            string findQuery = String.Format("select {0} from '{1}' where {2} {3} {4}", selectStmt, collection, whereClauseNotDeletedStmt, orderByClauseStmt, limitAndOffsetClause);

            JArray results = new JArray();
    
            bool validSelect = dbMgr.selectAllInto(results, findQuery);
    
            if (! validSelect) {
                return null;
            }
    
            return results;
        }

        public JArray findWithQueryParts(List<JSONStoreQuery> queryParts, string collectionName, JSONStoreQueryOptions options)
        {
            // check options
            if (options == null)
            {
                options = new JSONStoreQueryOptions();
            }
    
            //Filter:
            string selectStmt = selectStatement(options.filter);
    
            //Limit and Offset:
            string limitAndOffsetClause = limitAndOffsetClauseWithLimit(options.hasLimit, options.limit, options.offset);
    
            string orderByClauseStmt;
    
            if (limitAndOffsetClause == null) {
        
                //Negative limit edge case
                orderByClauseStmt = @"ORDER BY _id DESC ";

                if (Math.Ceiling((double)Math.Abs(options.offset)) > 0) 
                {
                    limitAndOffsetClause = buildLimitAndOffsetClauseWithLimit(options.limit, options.offset);
            
                } else {
            
                    limitAndOffsetClause = buildLimitClauseWithLimit(options.limit);
                }
        
            } else {
        
                orderByClauseStmt = orderByClause(options.sort);
            }
    
            string whereClauseStr = "";
    
            //Only add the where if a query was passed
            if (queryParts.Count > 0) {
                whereClauseStr = "where ";
            } else {
                whereClauseStr = String.Format("where {0}", JSONStoreConstants.JSON_STORE_FIELD_DELETED + " = 0");
            }
    
            List<string> allQueryParts = new List<string>();
    
            foreach(JSONStoreQuery queryPart in queryParts) {
        
                List<string> singleQueryPart = new List<string>();
        
                //LessThan
                string lessThanStr = whereClauseWithSymbol("<", queryPart.lessThan);
                if (lessThanStr.Length > 0) {
                    singleQueryPart.Add(lessThanStr);
                }
        
                //lessOrEqualThan
                string lessOrEqualThanStr = whereClauseWithSymbol("<=", queryPart.lessOrEqualThan);
                if (lessOrEqualThanStr.Length > 0) {
                    singleQueryPart.Add(lessOrEqualThanStr);
                }
        
                //greaterThan
                string greaterThanStr = whereClauseWithSymbol(">", queryPart.greaterThan);
                if (greaterThanStr.Length > 0) {
                    singleQueryPart.Add(greaterThanStr);
                }
        
                //greaterOrEqualThan
                string greaterOrEqualThanStr = whereClauseWithSymbol(">=", queryPart.greaterOrEqualThan);
                if (greaterOrEqualThanStr.Length > 0) {
                    singleQueryPart.Add(greaterOrEqualThanStr);
                }
        
                //like
                string likeStr = whereClauseWithStrFormat("[{0}] LIKE '%{1}%'", queryPart.like, false);
                if(likeStr.Length > 0) {
                    singleQueryPart.Add(likeStr);
                }
        
                //not like
                string notLikeStr = whereClauseWithStrFormat("[{0}] NOT LIKE '%{1}%'", queryPart.notLike, false);
                if(notLikeStr.Length > 0) {
                    singleQueryPart.Add(notLikeStr);
                }
        
                //rightLike
                string rightLikeStr = whereClauseWithStrFormat("[{0}] LIKE '{1}%\'", queryPart.rightLike, false);
                if (rightLikeStr.Length > 0) {
                    singleQueryPart.Add(rightLikeStr);
                }
        
                //not rightLike
                string notRightLikeStr = whereClauseWithStrFormat("[{0}] NOT LIKE '{1}%\'",queryPart.notRightLike, false);
                if (notRightLikeStr.Length > 0) {
                    singleQueryPart.Add(notRightLikeStr);
                }
        
                //leftLike
                string leftLikeStr = whereClauseWithStrFormat("[{0}] LIKE '%{1}'", queryPart.leftLike, false);
                if (leftLikeStr.Length > 0) {
                    singleQueryPart.Add(leftLikeStr);
                }
        
                //notLeftLike
                string notLeftLikeStr = whereClauseWithStrFormat("[{0}] NOT LIKE '%{1}'", queryPart.notLeftLike, false);
                if (notLeftLikeStr.Length > 0) {
                    singleQueryPart.Add(notLeftLikeStr);
                }
        
                //equal
                string equalStr = whereClauseWithStrFormat("( [{0}] = '{1}' OR [{2}] LIKE '%-@-{3}-@-%' OR [{4}] LIKE '%-@-{5}' OR [{6}] LIKE '{7}-@-%' )", queryPart.equal, true);
                if (equalStr.Length > 0) {
                    singleQueryPart.Add(equalStr);
                }
        
                //notEqual
                string notEqualStr = whereClauseWithStrFormat("( [{0}] != '{1}' AND [{2}] NOT LIKE '%-@-{3}-@-%' AND [{4}] NOT LIKE '%-@-{5}' AND [{6}] NOT LIKE '{7}-@-%' )", queryPart.notEqual, true);
                if (notEqualStr.Length > 0) {
                    singleQueryPart.Add(notEqualStr);
                }
        
                
                //in
                string inStr = whereClauseInWithArray(queryPart.inside, false);
                if (inStr.Length > 0) {
                    singleQueryPart.Add(inStr);
                }
        
                //not in
                string notInStr = whereClauseInWithArray(queryPart.notInside, true);
                if (notInStr.Length > 0)
                {
                    singleQueryPart.Add(notInStr);
                }
        
                //between
                string betweenStr = whereClauseBetweenWithArray(queryPart.between, false);
                if (betweenStr.Length > 0) {
                    singleQueryPart.Add(betweenStr);
                }
        
                //not between
                string notBetweenStr = whereClauseBetweenWithArray(queryPart.notBetween, true);
                if (notBetweenStr.Length > 0)
                {
                    singleQueryPart.Add(notBetweenStr);
                }
        
                //ids
                string idsStr = whereClauseForMultipleIds(queryPart.ids);
                if (idsStr.Length > 0) {
                    singleQueryPart.Add(idsStr);
                }
                
        
                singleQueryPart.Add(JSONStoreConstants.JSON_STORE_FIELD_DELETED + " = 0");

                string combined = "";
                foreach (string val in singleQueryPart)
                {
                    if (String.IsNullOrEmpty(combined))
                    {
                        combined = val;
                    }
                    else
                    {
                        combined = combined + " AND " + val;
                    }
                }
        
                allQueryParts.Add(combined);
            }

            string combinedOr = "";
                foreach (string val in allQueryParts)
                {
                    if (String.IsNullOrEmpty(combinedOr))
                    {
                        combinedOr = val;
                    }
                    else
                    {
                        combinedOr = combinedOr + " OR " + val;
                    }
                }
    
            if (allQueryParts.Count > 0) {
                whereClauseStr = whereClauseStr + String.Format("{0}", combinedOr);
            }
    
            string findQuery = String.Format("select {0} from '{1}' {2} {3} {4}",
                           selectStmt, collectionName, whereClauseStr, orderByClauseStmt, limitAndOffsetClause);
    
            JArray results = new JArray();
    
            bool validSelect = dbMgr.selectAllInto(results, findQuery);
    
            if (!validSelect) {
                return null;
            }
    
            return results;
        }

        public int count(string document)
        {
            int count = 0;
    
            string selectStmt = String.Format("select count(*) from '{0}' where _deleted = 0", document);
    
            //DLog(@"Finding local count wiht query -->%@", selectStmt);

            IDictionary<string, string> results;
    
            dbMgr.selectInto(out results, selectStmt);

            count = Int32.Parse(results["count(*)"]);
    
            return count;
        }

        public int dirtyCount(string document)
        {
            int count = 0;
    
            string whereClause = whereClauseForDirty();
    
            string selectStmt = String.Format("select count(*) from '{0}' where {1}", document, whereClause);
    
            //DLog(@"Finding local count wiht query -->%@", selectStmt);

            IDictionary<string, string> results;
    
            dbMgr.selectInto(out results, selectStmt);

            count = Int32.Parse(results["count(*)"]);
    
            return count;
        }

        public int countWithQuery(JObject query, string collection, bool exact)
        {
            int result = 0;

            string whereClauseNotDeletedStmt = whereClauseNotDeleted(query, " and ", exact);
    
            string countQuery = String.Format("select count(*) from '{0}' where {1}", collection, whereClauseNotDeletedStmt);

            //DLog(@"Performing count statement: %@", countQuery);

            IDictionary<string, string> results;

            bool validSelect = dbMgr.selectInto(out results, countQuery);

            if (!validSelect)
            {
                //DLog(@"validSelect: FALSE");
                return -1;
            }

            try
            {
                result = Int32.Parse(results["count(*)"]);
            }
            catch (Exception)
            {
                //DLog(@"Caught exception trying to get the count result, exception: %@", e);
                return -1;
            }
            
            return result;
        }


        public bool startTransaction()
        {
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
            return dbMgr.startTransaction();
        }

        public bool commitTransaction()
        {
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
            return dbMgr.commitTransaction();
        }

        public bool rollbackTransaction()
        {
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
            return dbMgr.rollbackTransaction();
        }

        public bool isOpen()
        {
            return dbMgr != null;
        }

        public bool close()
        {
            username = null;
            indexer = null;
            jsonSchemas = null;
            sharedManagerSingleton = null;
            bool closed = dbMgr.close();
            dbMgr = null;
            dbHasBeenKeyed = false;
            
            return closed;
        }

        public bool isDirty(string documentId, string collectionName)
        {
            string whereClause = whereClauseForId(documentId);
            string dirtyWhereClause = whereClauseForDirty();
    
            string dirtyQuery = String.Format("select {0} from '{1}' where {2} and {3}", JSONStoreConstants.JSON_STORE_FIELD_DIRTY, collectionName, dirtyWhereClause, whereClause);

            IDictionary<string, string> results;

            dbMgr.selectInto(out results, dirtyQuery);
    
            if (results.Count <= 0 ) {
                return false;
            } else {
                string val;
                return results.TryGetValue(JSONStoreConstants.JSON_STORE_FIELD_DIRTY, out val);
            }
        }

        public int destroyDbDirectory(Boolean destroyAll)
        {
            JSONStoreSecurityManager sec = JSONStoreSecurityManager.sharedManager();
            if(destroyAll) {
            	sec.clearKeys();
            } else {
            	sec.clearKey(username);
            }

            if (dbMgr == null) {
                dbMgr = new JSONStoreDBMgr(username);
            }

            dbMgr.destroyDBDirectory(destroyAll);

            return 0;
        }

        public int remove(JToken document, string collectionName, bool markDirty, bool exact)
        {
            int numMarkedDeleted = 0;

            JToken tok = ((JObject)document).GetValue(JSONStoreConstants.JSON_STORE_FIELD_ID);

            JArray results = null;
            if (tok != null)
            {
                JSONStoreQueryOptions options = new JSONStoreQueryOptions();
                options.exact = true;
                results = find(tok, collectionName, options);
            }
            else
            {
                JSONStoreQueryOptions qOptions = new JSONStoreQueryOptions();
                qOptions.exact = exact;
                results = find(document, collectionName, qOptions);
            }

            foreach (JObject token in results)
            {
                string id = token.GetValue(JSONStoreConstants.JSON_STORE_FIELD_ID).ToString();
        
                // If is marked dirty, then indicate the document should be delete on the next sync.
                // If not marked dirty, that means just remove the document from the local store.
                // If the document has been added to the local store but not yet synced to the server
                // then it should be removed from the local store regardless of what the markDirty
                // flag indicates (the server doesn't know about it yet)
                if (markDirty && !isAdded(id, collectionName)) {
            
                    IDictionary<string, object> setClauseDict = new Dictionary<string, object>();

                    setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DIRTY, DateTimeOffset.Now);
                    setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_OPERATION, JSONStoreConstants.JSON_STORE_OP_DELETE);
                    setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DELETED, 1);
            
            
                    string whereClause = whereClauseForId(id);
                    string setClause = queryFromDict(setClauseDict, ", ");
            
                    //Note that we don't actually delete here, we just mark the record as deleted so we can push the change to the adapter.
                    string updateStmt = String.Format("update '{0}' set {1} where ( {2} )", collectionName, setClause, whereClause);
            
                    if (dbMgr == null)
                    {
                        dbMgr = new JSONStoreDBMgr(username);
                    }
                    if (dbMgr.execute(updateStmt, setClauseDict.Values.ToArray())) {
                
                        numMarkedDeleted++;
                
                    } else {
                        //JSONStoreLoggerTrace(@"An error occured removing a record from database, collection: %@ _id: %d", collection, _id);
                    }
            
                } else {
            
                    // Since we are deleting a specific _id, this should always just return 1 unless and
                    // error occurred, in which case it will return -1
                    int numActualDeleted = deleteFromCollection(collectionName, id);
            
                    if (numActualDeleted > 0) {
                        numMarkedDeleted += numActualDeleted;
                    }
                }
            }
            return numMarkedDeleted;
        }

        public bool replace(JToken document, string collectionName, bool markDirty)
        {
            // get the schema (search fields and additional search fields)
            JSONStoreSchema jsonSchema = jsonSchemas[collectionName];

            // get the json data
            JToken json = ((JObject)document).GetValue(JSONStoreConstants.JSON_STORE_FIELD_JSON);

            // get a dictionary of index and values
            IDictionary<string, HashSet<string>> indexesAndValues = indexer.findIndexesFromSchema(jsonSchema, (JObject)json);

            int rowsUpdated = 0;
            
            string id = ((JObject)document).GetValue(JSONStoreConstants.JSON_STORE_FIELD_ID).ToString();

            if (isRemoved(id, collectionName)) {
                return false;
            }

            IDictionary<string, object> setClauseDict = new Dictionary<string, object>();

            foreach (KeyValuePair<string, HashSet<string>> entry in indexesAndValues)
            {
                string valString = "";
                foreach (string val in entry.Value)
                {
                    if (String.IsNullOrEmpty(valString))
                    {
                        valString = val;
                    }
                    else
                    {
                        valString = valString + "-@-" + val;
                    }
                    string key = String.Format("{0}", getDatabaseSafeSearchFieldName(entry.Key));
                    setClauseDict.Add(key, valString);
                }
            };

            MemoryStream ms = new MemoryStream();
            using (BsonWriter writer = new BsonWriter(ms))
            {
                ((JObject)json).WriteTo(writer);
            }
            setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_JSON, ms.ToArray());

            if (markDirty)
            {
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DIRTY, DateTimeOffset.Now);
            }
            else
            {
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DIRTY, 0);
            }

            if (!isAdded(id, collectionName))
            {
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_OPERATION, JSONStoreConstants.JSON_STORE_OP_UPDATE);
            }

            string whereClause = whereClauseForId(id);
            string setClause = queryFromDict(setClauseDict, ", ");

            string updateStmt = String.Format("update '{0}' set {1} where ( {2} )", collectionName, setClause, whereClause);

            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
            
            rowsUpdated = dbMgr.executeRowsModified(updateStmt, setClauseDict.Values.ToArray());
        
            return rowsUpdated > 0;
        }
  
        public JArray allDirtyInCollection(string collection)
        {
    
            string whereClause = whereClauseForDirty();
            string orderByClause = orderByDirty();
    
            string selectStmt = String.Format("select {0}, {1}, {2}, {3} from '{4}' where {5} {6}",
                            JSONStoreConstants.JSON_STORE_FIELD_ID,
                            JSONStoreConstants.JSON_STORE_FIELD_JSON,
                            JSONStoreConstants.JSON_STORE_FIELD_OPERATION,
                            JSONStoreConstants.JSON_STORE_FIELD_DIRTY,
                            collection,
                            whereClause,
                            orderByClause);
    
            JArray retArr = new JArray();

            bool worked = dbMgr.selectAllInto(retArr, selectStmt);
    
            if (! worked) {
               // JSONStoreLoggerTrace(@"All dirty operation failed, collection: %@", collection);
            }
    
            return retArr;
        }

        public bool markClean(string docId, string collectionName, string operation)
        {
            if (operation.Equals(JSONStoreConstants.JSON_STORE_OP_DELETE)) {
        
                return deleteFromCollection(collectionName, docId) > 0 ? true : false;
        
            } else {
        
                IDictionary<string, object> setClauseDict = new Dictionary<string, object>();
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DIRTY, 0);
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_DELETED, 0);
                setClauseDict.Add(JSONStoreConstants.JSON_STORE_FIELD_OPERATION, "");
        
                string setClauseStr = queryFromDict(setClauseDict, ", ");
                string whereClauseStr = whereClauseForId(docId);
        
                string updateStmt = String.Format("update '{0}' set {1} where {2}",
                                collectionName, setClauseStr, whereClauseStr);
        
                bool worked = dbMgr.execute(updateStmt, setClauseDict.Values.ToArray());
        
                if (! worked) {
                    //JSONStoreLoggerTrace(@"markClean operation failed, collection: %@, docId: %d, operation: %@", collection, docId, operation);
                }
        
                return worked;
            }
        }

        public bool clearTable(string collectionName)
        {
            string dropStmt = String.Format("DELETE FROM '{0}' WHERE 1", collectionName);
            return dbMgr.execute(dropStmt);
        }

        public bool setDatabaseKey(string username, string password)
        {
            //Need to derive the key from clear text
            JSONStoreSecurityManager jsonsecmanager = JSONStoreSecurityManager.sharedManager();
        
            string key = jsonsecmanager.getDPK(username, password);
        
            if (!String.IsNullOrEmpty(key)) 
            {
                bool worked;
                if (!dbHasBeenKeyed) {
            
                    if (dbMgr == null)
                    {
                        dbMgr = new JSONStoreDBMgr(username);
                    }
            
                    string pragmaKey = String.Format("PRAGMA key = \"x'{0}'\";", key);
                    worked = dbMgr.execute(pragmaKey);
            
                    if (worked) {
                
                        if (_checkSetKeyWorked()) {
                            dbHasBeenKeyed = true;
                        }
                    }
                } else {
            
                    //User gave us a good PW, but we've already keyed, so return true and no-op
                    worked = true;
                }
                return worked;
            } else {
            
                //JSONStoreLoggerTrace(@"Invalid password, pwd length: %d, security manager username: %@, username: %@", [password length], jsonsecmanager != nil ? jsonsecmanager.username : @"nil", self.username);
            
                return false;
            }
        }

        public bool changePassword(string oldPassword, string newPassword, string username)
        {
            JSONStoreSecurityManager secMgr = JSONStoreSecurityManager.sharedManager();
            return secMgr.changeOldPassword(username, oldPassword, newPassword);
        }

        private string schemaFromDict(IDictionary<string, string> schema) {
            string retVal = "";

            foreach(KeyValuePair<string, string> entry in schema)
            {
                 // do something with entry.Value or entry.Key
                if(retVal.Length > 0)
                    retVal = retVal + ", ";
                retVal = retVal + "'" + getDatabaseSafeSearchFieldName(entry.Key) + "' " + jsonToSqlSchemaType(entry.Value);
            }
    
            return retVal;
        }

        private bool validateExistingSchemaAgainst(string indexedColumns, string collectionName, string createPre, string createSuf) {
    
            // Get the create statement used to create the existing table using a special select statement
            string schemaSelect = String.Format("SELECT sql FROM sqlite_master WHERE type='table' AND name = '{0}'", collectionName);

            IDictionary<string, string> resultsDict;
    
            dbMgr.selectInto(out resultsDict, schemaSelect);
    
            string tableSchemaCreate = resultsDict["sql"];
            
            // Remove the table create Prefix and Suffix from the create statement we tried to use to create a new table
            // (and which was rejected because the table already exists).  We want just the part of the statement that specifies
            // the column names and types (e.g. "lastname" TEXT, "firstname" TEXT)
            tableSchemaCreate = tableSchemaCreate.Remove(0, createPre.Length);
            if (tableSchemaCreate.Length == createSuf.Length)
            {
                tableSchemaCreate = "";
            }
            else
            {
                tableSchemaCreate = tableSchemaCreate.Remove(tableSchemaCreate.Length - createSuf.Length - 2);
            }
    
            // The column names are separated by "," so break them into arrays where each element is a column name and type
            // Since column names are treated as case insensitive, normalize the string (to uppercase) first
            string[] currentTableSchema = tableSchemaCreate.ToUpper().Split(new char[]{','});

            string[] requestedTableSchema = indexedColumns.ToUpper().Split(new char[]{','});
    
            // Compare the current table's column names and types to what was requested.  If they have the same
            // column names and types, then the schema is the same.
            if (currentTableSchema.Length != requestedTableSchema.Length)
            {
                return false;
            }

            bool colMatch = false;
            foreach (string curColumn in currentTableSchema)
            {
                foreach (string reqColumn in requestedTableSchema)
                {
                    if(curColumn.Trim().Equals(reqColumn.Trim())) {
                        colMatch = true;
                        break;
                    }
                }
                if(!colMatch) {
                    return false;
                }
                colMatch = false;
            }

            return true;
        }

        private string jsonToSqlSchemaType(string jsonType) 
        {
            //SQLLite types taken from here: http://www.sqlite.org/datatype3.html
            //JSON types taken from here: http://tools.ietf.org/html/draft-zyp-json-schema-03#section-5.1
            //Note: No object or array b/c we say you can't index those.
            if(jsonType.Equals("string")) 
            {
                return "TEXT";
            } 
            else if(jsonType.Equals("number")) 
            {
                return "REAL";
            } 
            else if(jsonType.Equals("integer")) 
            {
                return "INTEGER";
            }
            else if (jsonType.Equals("boolean"))
            {
                return "INTEGER";
            }
            else
            {
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_PROVISION_TABLE_FAILURE);
            }
        }

        private string buildValueStr(int size)
        {
            string s = "";
    
            for (int i = 0; i < size; i++) {
        
                s = s + "?";
        
                if (i < (size - 1)) {
                    s = s + ", ";
                }
            }
    
            return s;
        }

        /*
         * Generate the 'select' statement
         */
        private string selectStatement(string[] filter)
        {
            if (filter == null || filter.Length == 0) 
            {
                //Default select columns
                return "[_id], [json]";
            }
    
            // there are filters
            string last = filter[filter.Length - 1];
    
            string selectStmt = "";
    
            foreach (string str in filter) {
                selectStmt = selectStmt + "[" + getDatabaseSafeSearchFieldName(str) + "]";
                if (str != last) {
                    selectStmt = selectStmt + ", ";
                }
            }
            return selectStmt;
        }

        /*
         * Generate the 'limit' and 'offset' clause
         */
        string limitAndOffsetClauseWithLimit(bool hasLimit, int limit, int offset)
        {
            string limitOffsetStr;
    
            if (!hasLimit) {
                //No limit specified
                limitOffsetStr = "";
            } else {
        
                //Limit with no offset
                if (limit < 0 ) {
            
                    //Negative limit case, get the 'last' limit records:
                    //select .... order by _id desc limit <limit opt>
                    return null;
                } else if (offset <= 0)  {
            
                    //Normal positive limit case, select .... LIMIT <limit opt>
                    limitOffsetStr = buildLimitClauseWithLimit(limit);
        
                } else {
            
                    //Limit and offset
                    limitOffsetStr = buildLimitAndOffsetClauseWithLimit(limit, offset);
                }
            }
    
            return limitOffsetStr;
        }

        /*
         * Genearte the 'limit' and 'offset' clause
         */
        private string buildLimitAndOffsetClauseWithLimit(int limit, int offset)
        {
            return String.Format("LIMIT {0} OFFSET {1}", Math.Ceiling((double)Math.Abs(limit)), Math.Ceiling((double)Math.Abs(offset)));
        }

        /*
         * Generate the 'limit' clause
         */
        private string buildLimitClauseWithLimit(int limit)
        {
            return String.Format("LIMIT {0}", Math.Ceiling((double)Math.Abs(limit)));
        }

        /*
         * Generate the 'order by" clause
         */
        private string orderByClause(JArray sort)
        {
            if (sort == null || sort.Count <= 0) {
                return "";
            }
    
            string sortStr = "ORDER BY ";
    
            foreach (JObject sortObject in sort) {

                string key = ((JProperty)sortObject.First).Name;
                string value = sortObject[key].ToString();

                sortStr = sortStr + String.Format("[{0}] {1}", getDatabaseSafeSearchFieldName(key), value);
        
                if (sortObject != sort.Last) {
                    sortStr = sortStr + ", ";
                }
            }
    
            return sortStr;
        }

        /*
         * Generate a "where" clause where the entry is not deleted...i.e. _deleted equals 0
         */
        private string whereClauseNotDeleted(JToken query, string delimiter, bool exact)
        {
            string queryStr = queryFromToken(query, delimiter, exact);
            string  retQuery = null;
    
            string deletedClause = JSONStoreConstants.JSON_STORE_FIELD_DELETED + " = 0";
    
            if (!String.IsNullOrEmpty(queryStr)) {
        
                retQuery = String.Format("{0} and {1}", queryStr, deletedClause);
        
            } else {
        
                retQuery = String.Format("{0}", deletedClause);
            }
    
            return retQuery;
        }

        private string whereClauseForMultipleIds(JArray docId)
        {
            string whereClauseStr = "";
    
            if(docId != null && docId.Count > 0) {
                List<string> returnList = new List<string>();
        
                foreach(JValue i in docId) {
                    string idStr = String.Format("{0} = {1}", JSONStoreConstants.JSON_STORE_FIELD_ID, i.ToString());
                    if(String.IsNullOrEmpty(whereClauseStr)) 
                    {
                        whereClauseStr = idStr;
                    } else 
                    {
                        whereClauseStr = whereClauseStr + " OR " + idStr;
                    }
                }
        
                whereClauseStr = String.Format("( {0} )", whereClauseStr);
            }
    
            return whereClauseStr;
        }

        /*
         * create the query string with key/value pairs
         */
        private string queryFromDict(IDictionary<string, object> query, string delimiter) 
        {
            List<string> retList = new List<string>();
            foreach(KeyValuePair<string, object> entry in query) {
                retList.Add(String.Format("[{0}] = ?", entry.Key));
            }
    
            string retVal = "";
            foreach (string str in retList)
            {
                if(String.IsNullOrEmpty(retVal)) {
                    retVal = retVal + str;
                } else {
                    retVal = retVal + delimiter + str;
                }
            }
            return retVal;
        }

        /*
         * create the query string with key/value pairs
         */
        private string queryFromToken(JToken query, string delimiter, bool exact)
        {
            List<string> retList = new List<string>();
            if (query.Type == JTokenType.Object)
            {
                foreach (JProperty entry in query.Children())
                {
                    retList.Add(handleValue(entry.Name, entry.Value, exact));
                }
            }
            else
            {
                // if no keys, then these are just id values
                retList.Add(handleValue(JSONStoreConstants.JSON_STORE_FIELD_ID, query, exact));
            }

            string retVal = "";
            foreach (string str in retList)
            {
                if(String.IsNullOrEmpty(retVal)) {
                    retVal = retVal + str;
                } else {
                    retVal = retVal + delimiter + str;
                }
            }
    
            return retVal;
        }

        /*
         * Creates the values portion of the query string
         */
        private string handleValue(string name, JToken value, bool exact)
        {
            string retVal = "";

            // get the safe key value
            string key = getDatabaseSafeSearchFieldName(name);
            string obj = getDatabaseSafeSearchValue(value);

            // create the fuzzy search string
            if (!exact)
            {
                retVal = String.Format("[{0}] LIKE '%{1}%'", getDatabaseSafeSearchFieldName(name), obj);
            }
            else
            {
                // create the exact search string as follows...

                /*
                (
                    [customers.fn] =  "carlos"
                    or [customers.fn] like "%-@-carlos-@-%"
                    or [customers.fn] like "%-@-carlos"
                    or [customers.fn] like "carlos-@-%"
                )
                */

                retVal = String.Format("( [{0}] = \"{1}\"  or [{2}] LIKE \"%-@-{3}-@-%\" or [{4}] LIKE \"%-@-{5}\" or [{6}] LIKE \"{7}-@-%\" )", key, obj, key, obj, key, obj, key, obj);
            }
            return retVal;
        }

        /*
         * returns true if a document has been added to the collection
         */
        private bool isAdded(string id, string collectionName)
        {
            string addedQuery = String.Format("select {0} from '{1}' where {2}", JSONStoreConstants.JSON_STORE_FIELD_OPERATION, collectionName, whereClauseForId(id));

            IDictionary<string, string> results;
    
            dbMgr.selectInto(out results, addedQuery);
    
            if (results.Count <= 0) 
            {
                return false;
            } 
            else 
            {
                string operation = results[JSONStoreConstants.JSON_STORE_FIELD_OPERATION];
                return operation.Equals(JSONStoreConstants.JSON_STORE_OP_ADD);
            }
        }

        /*
         * returns true if a document with a given id has been removed from the collection
         */
        private bool isRemoved(string id, string collectionName)
        {
            string removedQuery = String.Format("select {0} from '{1}' where {2}", JSONStoreConstants.JSON_STORE_FIELD_DELETED, collectionName, whereClauseForId(id));
            
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }

            IDictionary<string, string> results;

            dbMgr.selectInto(out results, removedQuery);

            if (results.Count <= 0)
            {
                return true;
            }
            else
            {
                return Int32.Parse(results[JSONStoreConstants.JSON_STORE_FIELD_DELETED]) != 0;
            }
        }

        /*
         * Does the actual delete statement in the database, this should only be called by the code that cleans up the queue,
         *if you just want to mark a record as deleted you should call remove:(NSDictionary*)query inCollection:(NSString*) collection
         */
        private int deleteFromCollection(string collectionName, string id)
        {
            string deleteStmt = String.Format("delete from '{0}' where ( {1} )", collectionName, whereClauseForId(id));
    
            if (dbMgr == null)
            {
                dbMgr = new JSONStoreDBMgr(username);
            }
    
            return dbMgr.executeRowsModified(deleteStmt);
        }

        /*
         * generates a 'where' caluse for a simple id parameter
         */
        private string whereClauseForId(string docId)
        {
            return String.Format("{0} = {1}", JSONStoreConstants.JSON_STORE_FIELD_ID, docId);
        }

        /*
         * generates a 'where" cause for dirty documents
         */
        private string whereClauseForDirty()
        {
            return String.Format("{0} > 0", JSONStoreConstants.JSON_STORE_FIELD_DIRTY);
        }

        /*
         * generates an 'order by" for dirty documents
         */
        private string orderByDirty()
        {
            return String.Format("order by {0}", JSONStoreConstants.JSON_STORE_FIELD_DIRTY);
        }

        /*
         * returns a key in a safe format, no special characters, no '.'
         */
        private string getDatabaseSafeSearchFieldName(string name)
        {
            if (name == null)
            {
                return null;
            }

            foreach (String ch in DATABASE_SPECIAL_CHARACTERS)
            {
                name = name.Replace(ch, "");
            }

            return name.Replace('.', '_');
        }

        /*
        * returns a value in a safe format, convert boolean
        */
        private string getDatabaseSafeSearchValue(JToken value)
        {
            string retVal = "";
                        
            // if boolean, convert to 1 or 0, else just use the string value
            if (value.Type == JTokenType.Boolean)
            {
                if (value.ToString().ToLower().Equals("true"))
                {
                    retVal = "1";
                }
                else
                {
                    retVal = "0";
                }

            }
            else
            {
                retVal = value.ToString();
            }
            return retVal;
        }

        // returns a where clause with the given symbol, using the values from the array
        private string whereClauseWithSymbol(string symbol, JArray array)
        {
            string clause = "";
            if (array != null)
            {
                foreach (JObject obj in array)
                {
                    foreach(JProperty searchField in obj.Children()) 
                    {
                        string val = String.Format("[{0}] {1} {2}", getDatabaseSafeSearchFieldName(searchField.Name), symbol, getDatabaseSafeSearchValue(searchField.Value));

                        if (String.IsNullOrEmpty(clause))
                        {
                            clause = val;
                        }
                        else
                        {
                            clause = clause + " AND " + val;
                        }
                    }
                }
            }
            return clause;
        }

        // returns a where clause for the given formatted string, using the values from the array
        string whereClauseWithStrFormat(string strFmt, JArray array, bool exact)
        {
            string clause = "";
            if (array != null)
            {
                foreach(JObject obj in array)
                {
                    foreach (JProperty searchField in obj.Children())
                    {
                        string val = "";
                        string value = getDatabaseSafeSearchValue(searchField.Value);
                        string name = getDatabaseSafeSearchFieldName(searchField.Name);

                        if (!exact)
                        {
                            val = String.Format(strFmt, name, value);
                        }
                        else
                        {
                            val = String.Format(strFmt, name, value, name, value, name, value, name, value);
                        }

                        if (String.IsNullOrEmpty(clause))
                        {
                            clause = val;
                        }
                        else
                        {
                            clause = clause + " AND " + val;
                        }
                    }
                }
            }
            return clause;
        }

        // returns a where clause for the given array
        private string whereClauseInWithArray(JArray array, bool not)
        {
            string clause = "";
            if (array != null)
            {
                foreach (JObject obj in array)
                {   
                    foreach (JProperty searchField in obj.Children())
                    {
                        string val = "";
                        List<string> valuesList = new List<string>();
            
                        foreach (JToken value in searchField.Value) {
                            valuesList.Add(String.Format("'{0}'", getDatabaseSafeSearchValue(value)));
                        }

                        string values = "";
                        foreach (string value in valuesList)
                        {
                            if (String.IsNullOrEmpty(values))
                            {
                                values = value;
                            }
                            else
                            {
                                values = values + "," + value;
                            }
                        }
            
                        val = String.Format("[{0}] {1} in ({2})", getDatabaseSafeSearchFieldName(searchField.Name), not ? "NOT" : "", values);

                        if (String.IsNullOrEmpty(clause))
                        {
                            clause = val;
                        }
                        else
                        {
                            clause = clause + " AND " + val;
                        }
                    }
                }
            }
            return clause;
        }

        // return a where clause for the between statement, using the values in the array
        private string whereClauseBetweenWithArray(JArray array, bool not)
        {
            string clause = "";
            if (array != null)
            {
                foreach (JObject obj in array)
                {
                    foreach (JProperty searchField in obj.Children())
                    {
                        string val = "";

                        JArray betweenValuesArr = (JArray)searchField.Value;

                        if (betweenValuesArr.Count == 2)
                        {
                            val = String.Format("[{0}] {1} BETWEEN {2} AND {3}", getDatabaseSafeSearchFieldName(searchField.Name), not ? "NOT" : "", getDatabaseSafeSearchValue(betweenValuesArr[0]), getDatabaseSafeSearchValue(betweenValuesArr[1]));
                            if (String.IsNullOrEmpty(clause))
                            {
                                clause = val;
                            }
                            else
                            {
                                clause = clause + " AND " + val;
                            }
                        }
                    }
                }
            }
            return clause;
        }

        private bool _checkSetKeyWorked()
        {
            IDictionary<string, string> resultsDict;

            return dbMgr.selectInto(out resultsDict, "select count(*) from sqlite_master;");
        }
    }
}