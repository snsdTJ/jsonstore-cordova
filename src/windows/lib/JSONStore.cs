/*
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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Newtonsoft.Json.Linq;


namespace JSONStoreWin8Lib.JSONStore
{
    public class JSONStore
    {
        // bool that determine of a user started transaction is in progress
        public static bool transactionInProgress { get; set; }

        // stores the collection objects for quick access
        private static IDictionary<string, JSONStoreCollection> globalJSONStoreCollectionAccessors;

        // lock object to prevent multiple threads trying to access SQLite at once
        public static System.Object lockThis = new System.Object();

        /**
         * Provides access to the collections inside the store, and creates them if they do not already exist.
         */
        public static bool openCollections(JSONStoreCollection[] collections, JSONStoreProvisionOptions options)
        {
            // if the collection dictionary is not already created, then create one
            if (globalJSONStoreCollectionAccessors == null)
            {
                globalJSONStoreCollectionAccessors = new Dictionary<string, JSONStoreCollection>();
            }

            // cannot open a collection during a transaction, throw exception
            if (transactionInProgress)
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE_DURING_INIT, code: %d", rc);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE_DURING_INIT);
            }

            // if a password is passed in, then we need to take steps to secure the database
            if (!String.IsNullOrEmpty(options.collectionPassword))
            {
                // create a new security manager for the given username
                JSONStoreSecurityManager security = JSONStoreSecurityManager.sharedManager();

                // check if the key is already store, if so, we do nothing more here
                if (!security.isKeyStored(options.username))
                {
                    // create and store the key
                    bool storeDPKworked = storeDataProtectionKey(options.username, options.localKeyGen ? "" : options.secureRandom, options.collectionPassword, security);

                    if (!storeDPKworked)
                    {
                        //JSONStoreLoggerError(@"Error: JSON_STORE_STORE_DATA_PROTECTION_KEY_FAILURE, code: %d, username: %@, salt length: %d, dpkClear length: %d, cbkClear length: %d, securityMgr username: %@", rc, options.username, salt != nil ? [salt length] : 0, options.secureRandom != nil ? [options.secureRandom length] : 0, options.password != nil ? [options.password length] : 0, secMgr != nil ? secMgr.username : @"nil");
                        throw new JSONStoreException(JSONStoreConstants.JSON_STORE_STORE_DATA_PROTECTION_KEY_FAILURE);
                    }
                }
            }

            // loop through each collection and attempt to open
            foreach (JSONStoreCollection collection in collections)
            {
                int rc = provisionCollection(collection.collectionName, collection.searchFields, collection.additionalSearchFields,
                    options.username, options.collectionPassword, collection.dropFirst);

                // determine if collection is new or was reopened
                if (rc == JSONStoreConstants.JSON_STORE_RC_OK || rc == JSONStoreConstants.JSON_STORE_PROVISION_TABLE_EXISTS)
                {
                    collection.wasReopened = rc > 0 ? true : false;

                    // store the collection in the accessor
                    if (!globalJSONStoreCollectionAccessors.ContainsKey(collection.collectionName))
                    {
                        globalJSONStoreCollectionAccessors.Add(collection.collectionName, collection);
                    }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /**
         * Provides an accessor to the collection if the collection exists. This method depends on init being called first, with the collection name requested.
         */
        public static JSONStoreCollection getCollectionWithName(string collectionName) 
        {
            //Returns null if the collection does not exist in the hash map
            if (globalJSONStoreCollectionAccessors != null)
            {
                JSONStoreCollection collection;
                // will try to get the collection, if it does not exist null will be returned
                globalJSONStoreCollectionAccessors.TryGetValue(collectionName, out collection);
                return collection;
            }
            return null;
        }

        /**
         * Locks access to all the collections until init is called.
         */
        public static bool closeAllCollections() {
            bool worked = false;

            // if transaction in process, cannot close, throw exception
            if (transactionInProgress)
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE_DURING_CLOSE_ALL, code: %d", JSON_STORE_TRANSACTION_FAILURE_DURING_CLOSE_ALL);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE_DURING_CLOSE_ALL);
            }
    
            JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();

            if (store == null)
            {
                // if can't get a store, then we are already closed
                worked = true;
            }
            else if (!store.isOpen())
            {
                // already closed!
                worked = true;
            }
            else
            {
                // not closed, so do the close
                worked = store.close();
            }
            
            if (worked) {
                // clear the collections
                globalJSONStoreCollectionAccessors = null;
            }
            else {
                //JSONStoreLoggerError(@"Error: JSON_STORE_ERROR_CLOSING_ALL, code: %d", rc);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_ERROR_CLOSING_ALL);
            }
            return worked;
        }

        /**
         * Changes the password associated with the security artifacts used to provide data encryption.
         */
        public static bool changeCurrentPassword(string oldPassword, string newPassword, string username) 
        {

             // get the shared manager
            JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();

            if (store == null)
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_DATABASE_NOT_OPEN, code: %d", rc);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_DATABASE_NOT_OPEN);
            }

            bool worked = store.changePassword(oldPassword, newPassword, username);
        
            if (!worked) 
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_ERROR_CHANGING_PASSWORD, code: %d, username: %@, newPwdLength: %d, oldPwdLength: %d", rc, username, [newPassword length], [oldPassword length]);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_ERROR_CHANGING_PASSWORD);
            }
    
            return worked;
        }

        /**
         * Removes a stored collection
         */
        public static void removeAccessor(string collectionName) 
        {
            globalJSONStoreCollectionAccessors.Remove(collectionName);
        }

        /**
         * Destory data in a collection
         */
        public static bool destroyData(string username)
        {
        	Boolean destroyAll = true;
            // cannot destory if transaction in progress, throw exception
            if (transactionInProgress)
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE_DURING_DESTROY, code: %d", rc);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE_DURING_DESTROY);
            }
 
 			JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();
            if(!String.IsNullOrEmpty(username)){
               destroyAll = false;
            } 

            if (store == null && !String.IsNullOrEmpty(username)) 
            {
                // if store is null, try to find with set username
                store = JSONStoreSQLLite.sharedManager(username);
            } else if(store == null && String.IsNullOrEmpty(username)) {
            	//if store is null, try to find with default username
                store = JSONStoreSQLLite.sharedManager(JSONStoreConstants.JSON_STORE_DEFAULT_USER);
            }

            if (store != null && destroyClearKeyChainAndCloseWithAccessor(store, destroyAll))
            {
                return closeAllCollections();
            }
            else
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_ERROR_DURING_DESTROY, code: %d, accessor user: %@", rc, accessor != nil ? accessor.username : @"nil");
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_ERROR_DURING_DESTROY);
            }
        }

        /**
         * start a user transaction
         */
        public static bool startTransaction()
        {
            lock (lockThis)
            {
                bool worked = true;

                // get the shared manager
                JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();

                if (store == null)
                {
                    //JSONStoreLoggerError(@"Error: JSON_STORE_DATABASE_NOT_OPEN, code: %d", rc);
                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_DATABASE_NOT_OPEN);
                }

                if (transactionInProgress)
                {

                    //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_IN_PROGRESS, code: %d", rc);
                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_IN_PROGRESS);

                }
                else
                {

                    worked = store.startTransaction();

                    if (!worked)
                    {
                        transactionInProgress = false;

                        //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE, code: %d", rc);
                        throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE);

                    }
                    else
                    {
                        transactionInProgress = true;
                    }
                }

                //JSONStoreLoggerAnalytics(username != nil ? username : @"", @"", @"startTransaction", startTime, rc);

                return worked;
            }
        }

        /**
         * Committ the user transaction
         */
        public static bool commitTransaction()
        {
            lock (lockThis)
            {
                bool worked = true;

                // get the shared manager
                JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();

                if (store == null)
                {
                    //JSONStoreLoggerError(@"Error: JSON_STORE_DATABASE_NOT_OPEN, code: %d", rc);
                    // problem getting the shared manager for this username
                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_DATABASE_NOT_OPEN);
                }

                string username = store.username;

                //long long startTime = wlGetTimeIntervalSince1970();


                if (!transactionInProgress)
                {
                    worked = false;

                    //JSONStoreLoggerError(@"Error: JSON_STORE_NO_TRANSACTION_IN_PROGRESS, code: %d", rc);

                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_NO_TRANSACTION_IN_PROGRESS);

                }
                else
                {
                    lock (lockThis)
                    {
                        worked = store.commitTransaction();
                    }

                    if (!worked)
                    {

                        transactionInProgress = false;

                        //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE, code: %d", rc);
                        throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE);

                    }

                    transactionInProgress = false;
                }

                //JSONStoreLoggerAnalytics(username != nil ? username : @"", @"", @"commitTransaction", startTime, rc);

                return worked;
            }
        }

        /**
         * Rollback user transaction
         */
        public static bool rollbackTransaction()
        {
            lock (lockThis)
            {
                bool worked = true;

                // get the shared manager
                JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager();

                if (store == null)
                {
                    //JSONStoreLoggerError(@"Error: JSON_STORE_DATABASE_NOT_OPEN, code: %d", rc);
                    // problem getting the shared manager for this username
                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_DATABASE_NOT_OPEN);
                }

                if (!transactionInProgress)
                {
                    worked = false;

                    //JSONStoreLoggerError(@"Error: JSON_STORE_NO_TRANSACTION_IN_PROGRESS, code: %d", rc);
                    throw new JSONStoreException(JSONStoreConstants.JSON_STORE_NO_TRANSACTION_IN_PROGRESS);
                }
                else
                {

                    worked = store.rollbackTransaction();

                    if (!worked)
                    {
                        transactionInProgress = false;

                        //JSONStoreLoggerError(@"Error: JSON_STORE_TRANSACTION_FAILURE, code: %d", rc);
                        throw new JSONStoreException(JSONStoreConstants.JSON_STORE_TRANSACTION_FAILURE);
                    }
                    transactionInProgress = false;
                }

                //JSONStoreLoggerAnalytics(username != nil ? username : @"", @"", @"commitTransaction", startTime, rc);

                return worked;
            }
        }

        /**
         * Return info on the database file
         */
        public static async Task <JArray> fileInfo()
        {
            JArray results = new JArray();

            // get the db directory
            var dbDir = await ApplicationData.Current.LocalFolder.GetFolderAsync(JSONStoreDBMgr.DB_SUB_DIR);
            StorageFolder dbDirectory = await StorageFolder.GetFolderFromPathAsync(dbDir.Path);

            // loop through all the files to gather info
            IEnumerable<IStorageFile> files = await dbDirectory.GetFilesAsync();

            foreach (IStorageFile anyfile in files)
            {
                try
                {
                    //create and read from the temp file
                    StorageFile tempFile = await anyfile.CopyAsync(ApplicationData.Current.TemporaryFolder, anyfile.Name, NameCollisionOption.GenerateUniqueName);
                    byte[] fileBytes = new byte[6];
                    ulong fileSize;

                    using (IRandomAccessStreamWithContentType stream = await tempFile.OpenReadAsync())
                    {
                        fileSize = stream.Size;
                        using (DataReader reader = new DataReader(stream))
                        {
                            await reader.LoadAsync((uint)6);
                            reader.ReadBytes(fileBytes);
                        }
                    }
                              
                    //delete the temp file
                    await tempFile.DeleteAsync();

                    string byteString = System.Text.Encoding.UTF8.GetString(fileBytes, 0, 6);
                    bool foundSqlite = false;
                    if (byteString.Equals("SQLite"))
                    {
                        foundSqlite = true;
                    }
                    
                    // remove the extension
                    string name = anyfile.Name.Replace(JSONStoreConstants.JSON_STORE_DB_FILE_EXTENSION, "");

                    // create the JObject return value
                    JObject result = new JObject();
                    result.Add(JSONStoreConstants.JSON_STORE_KEY_FILE_NAME, name);
                    result.Add(JSONStoreConstants.JSON_STORE_KEY_FILE_SIZE, fileSize);
                    result.Add(JSONStoreConstants.JSON_STORE_KEY_FILE_IS_ENCRYPTED, !foundSqlite);

                    // add result to array
                    results.Add(result);
                }
                catch (Exception)
                {
                    throw new JSONStoreException("Error getting file attributes");
                }
   
            }
            return results;
            
        }

        private static int provisionCollection(string collectionName, IDictionary<string, string> searchFields, IDictionary<string,
           string> additionalSearchFields, string username, string password, bool dropFirst)
        {
            // if no username set, use the default
            if (String.IsNullOrEmpty(username))
            {
                username = JSONStoreConstants.JSON_STORE_DEFAULT_USER;
            }

            // get the shared manager
            JSONStoreSQLLite store = JSONStoreSQLLite.sharedManager(username);

            if (store == null)
            {
                //JSONStoreLoggerError(@"Error: JSON_STORE_USERNAME_MISMATCH, code: %d, username passed: %@, accessor username: %@, collection name: %@", JSON_STORE_USERNAME_MISMATCH, username, accessor != nil ? accessor.username : @"nil", collectionName);
                throw new JSONStoreException(JSONStoreConstants.JSON_STORE_USERNAME_MISMATCH);
            }

            if (!String.IsNullOrEmpty(password))
            {
                // set the key for the database
                lock (lockThis)
                {
                    if (!store.setDatabaseKey(username, password))
                    {
                        //JSONStoreLoggerError(@"Error: JSON_STORE_PROVISION_KEY_FAILURE, code: %d, checkForSecurityUpgrade return code: %d, setDBKeyWorked: %@", JSON_STORE_PROVISION_KEY_FAILURE, rc, setDBKeyWorked ? @"YES" : @"NO");
                        throw new JSONStoreException(JSONStoreConstants.JSON_STORE_PROVISION_KEY_FAILURE);
                    }
                }
            }

            int rc = 0;
            lock (lockThis)
            {
                // drop the table if needed
                if (dropFirst)
                {
                    store.dropTable(collectionName);
                }

                // provision the collection for the name, searchFields, and additionalSearchFields
                
                rc = store.provisionCollection(collectionName, new JSONStoreSchema(searchFields, additionalSearchFields));

                if (rc < JSONStoreConstants.JSON_STORE_RC_OK)
                {
                    //JSONStoreLoggerError(@"Error: JSON_STORE_EXCEPTION, code: %d, username: %@, accessor username: %@, collection name: %@, searchFields: %@, additionalSearchFields: %@", rc, username, accessor != nil ? accessor.username : @"nil", collectionName, searchFields, additionalIndexes);

                    store.close();

                    throw new JSONStoreException(rc);
                }
            }

            return rc;
        }

        private static bool destroyClearKeyChainAndCloseWithAccessor(JSONStoreSQLLite store, Boolean destroyAll)
        {
            int rc = 0;
            lock (lockThis)
            {
                rc = store.destroyDbDirectory(destroyAll);
                store.close();
            }

            return (rc == 0) ? true : false;
        }

        private static bool storeDataProtectionKey(string username, string secureRandom, string password, JSONStoreSecurityManager securityMgr)
        {
            bool worked = false;

            // generate a random salt
            IBuffer salt = JSONStoreSecurityManager.generateRandom(JSONStoreConstants.JSON_STORE_DEFAULT_SALT_SIZE);
            worked = securityMgr.storeDPK(username, password, secureRandom, salt, false);
    
            if (worked && JSONStoreConstants.JSON_STORE_DEFAULT_USER.Equals(username)) 
            {
                //updateSecurityVersion();
            }

            return worked;
        }
    }
}
