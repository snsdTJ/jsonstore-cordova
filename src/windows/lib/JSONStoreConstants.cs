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

namespace JSONStoreWin8Lib.JSONStore
{
    public class JSONStoreConstants
    {
        public const int JSON_STORE_RC_OK = 0;
        public const int JSON_STORE_RC_JS_TRUE = 1; //Emulates a boolean in JavaScript
        public const int JSON_STORE_RC_JS_FALSE = 0; //Emulates a boolean in JavaScript

        public const string JSON_STORE_FIELD_JSON = "json";
        public const string JSON_STORE_FIELD_DIRTY = "_dirty";
        public const string JSON_STORE_FIELD_OPERATION = "_operation";
        public const string JSON_STORE_FIELD_DELETED = "_deleted";
        public const string JSON_STORE_FIELD_ID = "_id";

        public const string JSON_STORE_OP_ADD = "add";
        public const string JSON_STORE_OP_STORE = "store";
        public const string JSON_STORE_OP_DELETE = "remove";
        public const string JSON_STORE_OP_UPDATE = "replace";

        public const int JSON_STORE_DEFAULT_SALT_SIZE = 32;
        public const int JSON_STORE_DEFAULT_DPK_SIZE = 32;
        public const int JSON_STORE_DEFAULT_IV_SIZE = 16;
        public const int JSON_STORE_DEFAULT_PBKDF2_ITERATIONS = 10000;

        public const int JSON_STORE_DATABASE_NOT_OPEN = -50;

        public const int JSON_STORE_PROVISION_TABLE_EXISTS = 1;
        public const int JSON_STORE_PROVISION_TABLE_FAILURE = -1;
        public const int JSON_STORE_PROVISION_TABLE_SCHEMA_MISMATCH = -2;
        public const int JSON_STORE_PROVISION_KEY_FAILURE = -3;

        public const int JSON_STORE_PERSISTENT_STORE_FAILURE = -1;
        
        public const int JSON_STORE_INVALID_OFFSET = -9;
        public const int JSON_STORE_INVALID_SEARCH_FIELD = 22;
        public const int JSON_STORE_INVALID_JSON_STRUCTURE = -20;

        public const int JSON_STORE_ERROR_DURING_DESTROY = 25;
        public const int JSON_STORE_ERROR_CLOSING_ALL = 23;
        public const int JSON_STORE_ERROR_CHANGING_PASSWORD = 24;
        public const int JSON_STORE_ERROR_CLEARING_COLLECTION = 26;

        public const int JSON_STORE_TRANSACTION_IN_PROGRESS = -41;
        public const int JSON_STORE_NO_TRANSACTION_IN_PROGRESS = -42;
        public const int JSON_STORE_TRANSACTION_FAILURE = -43;
        public const int JSON_STORE_TRANSACTION_FAILURE_DURING_DESTROY = -46;
        public const int JSON_STORE_TRANSACTION_FAILURE_DURING_INIT = -44;
        public const int JSON_STORE_TRANSACTION_FAILURE_DURING_REMOVE_COLLECTION = -47;
        public const int JSON_STORE_TRANSACTION_FAILURE_DURING_CLOSE_ALL = -45;

        public const int JSON_STORE_COULD_NOT_MARK_DOCUMENT_PUSHED = 15;

        public const int JSON_STORE_STORE_DATA_PROTECTION_KEY_FAILURE = -21;
        public const int JSON_STORE_REMOVE_WITH_QUERIES_FAILURE = -22;

        public const string JSON_STORE_DEFAULT_USER = "jsonstore";

        public const string JSON_STORE_FILE_ENCRYPTED = "file is encrypted";

        public const int JSON_STORE_USERNAME_MISMATCH = -6;
        public const int JSON_STORE_REPLACE_DOCUMENTS_FAILURE = -23;

        // advancedQuery types
        public const string JSON_STORE_QUERY_LIKE = "like";
        public const string JSON_STORE_QUERY_NOT_LIKE = "notLike";
        public const string JSON_STORE_QUERY_RIGHT_LIKE = "rightLike";
        public const string JSON_STORE_QUERY_NOT_RIGHT_LIKE = "notRightLike";
        public const string JSON_STORE_QUERY_LEFT_LIKE = "leftLike";
        public const string JSON_STORE_QUERY_NOT_LEFT_LIKE = "notLeftLike";
        public const string JSON_STORE_QUERY_LESSTHAN = "lessThan";
        public const string JSON_STORE_QUERY_LESSTHANEQUALS = "lessOrEqualThan";
        public const string JSON_STORE_QUERY_GREATERTHAN = "greaterThan";
        public const string JSON_STORE_QUERY_GREATERTHANEQUALS = "greaterOrEqualThan";
        public const string JSON_STORE_QUERY_EQUALS = "equal";
        public const string JSON_STORE_QUERY_NOT_EQUALS = "notEqual";
        public const string JSON_STORE_QUERY_INSIDE = "inside";
        public const string JSON_STORE_QUERY_NOT_INSIDE = "notInside";
        public const string JSON_STORE_QUERY_BETWEEN = "between";
        public const string JSON_STORE_QUERY_NOT_BETWEEN = "notBetween";

        public const string JSON_STORE_KEY_DPK = "dpk";
        public const string JSON_STORE_KEY_SALT = "jsonSalt";
        public const string JSON_STORE_KEY_IV = "iv";
        public const string JSON_STORE_KEY_ITERATIONS = "iterations";
        public const string JSON_STORE_KEY_VERSION = "version";
        public const string JSON_STORE_KEY_VERSION_NUMBER = "1.0";
        public const string JSON_STORE_KEY_DOCUMENT_ID = "JSONStoreKey";

        public const string JSON_STORE_KEY_FILE_NAME = "name";
        public const string JSON_STORE_KEY_FILE_SIZE = "size";
        public const string JSON_STORE_KEY_FILE_IS_ENCRYPTED = "isEncrypted";

        public const string JSON_STORE_DB_FILE_EXTENSION = ".sqlite";



    }
}
