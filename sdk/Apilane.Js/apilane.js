/**
 * Apilane JavaScript SDK
 *
 * The official JavaScript SDK for the Apilane backend-as-a-service platform.
 * Works in browsers and Node.js 18+ (requires native fetch).
 *
 * @module apilane
 * @version 1.0.0
 * @license MIT
 * @see https://docs.apilane.com/developer_guide/sdk/
 */

// ============================================================================
// Enums
// ============================================================================

/** Filter comparison operators. */
export const FilterOperator = Object.freeze({
    equal: 'equal',
    notequal: 'notequal',
    greater: 'greater',
    greaterorequal: 'greaterorequal',
    less: 'less',
    lessorequal: 'lessorequal',
    startswith: 'startswith',
    endswith: 'endswith',
    contains: 'contains',
    notcontains: 'notcontains',
});

/** Logical operators for combining filters. */
export const FilterLogic = Object.freeze({
    AND: 'AND',
    OR: 'OR',
});

/** Validation error codes returned by the API. */
export const ValidationError = Object.freeze({
    ERROR: 'ERROR',
    UNAUTHORIZED: 'UNAUTHORIZED',
    UNCONFIRMED_EMAIL: 'UNCONFIRMED_EMAIL',
    REQUIRED: 'REQUIRED',
    NOT_FOUND: 'NOT_FOUND',
    INVALID_FILTER_PARAMETER: 'INVALID_FILTER_PARAMETER',
    INVALID_SORT_PARAMETER: 'INVALID_SORT_PARAMETER',
    INVALID_GROUPBY_PARAMETER: 'INVALID_GROUPBY_PARAMETER',
    NO_PROPERTIES_PROVIDED: 'NO_PROPERTIES_PROVIDED',
    EMPTY_BODY: 'EMPTY_BODY',
    NO_ID_PROVIDED: 'NO_ID_PROVIDED',
    NO_RECORDS_FOUND_TO_DELETE: 'NO_RECORDS_FOUND_TO_DELETE',
    FILE_NOT_FOUND: 'FILE_NOT_FOUND',
    VALIDATION: 'VALIDATION',
    UNIQUE_CONSTRAINT_VIOLATION: 'UNIQUE_CONSTRAINT_VIOLATION',
    FOREIGN_KEY_CONSTRAINT_VIOLATION: 'FOREIGN_KEY_CONSTRAINT_VIOLATION',
    CONNECTION_ALREADY_EXISTS: 'CONNECTION_ALREADY_EXISTS',
    CONNECTION_REQUEST_ALREADY_PENDING: 'CONNECTION_REQUEST_ALREADY_PENDING',
    NOT_CONNECTED: 'NOT_CONNECTED',
    SERVICE_UNAVAILABLE: 'SERVICE_UNAVAILABLE',
    USER_NOT_FOUND: 'USER_NOT_FOUND',
    RATE_LIMIT_EXCEEDED: 'RATE_LIMIT_EXCEEDED',
});

/** Transaction operation types. */
export const TransactionAction = Object.freeze({
    Post: 'Post',
    Put: 'Put',
    Delete: 'Delete',
    Custom: 'Custom',
});

/** Aggregate function types for stats queries. */
export const DataAggregates = Object.freeze({
    Min: 'Min',
    Max: 'Max',
    Count: 'Count',
    Sum: 'Sum',
    Avg: 'Avg',
});

// ============================================================================
// Error Model
// ============================================================================

/**
 * Represents an API error returned by Apilane.
 */
export class ApilaneError {
    /**
     * @param {object} [init]
     * @param {string} [init.Code]
     * @param {string} [init.Message]
     * @param {string|null} [init.Property]
     * @param {string|null} [init.Entity]
     */
    constructor(init = {}) {
        /** @type {string} */
        this.Code = init.Code ?? ValidationError.ERROR;
        /** @type {string} */
        this.Message = init.Message ?? '';
        /** @type {string|null} */
        this.Property = init.Property ?? null;
        /** @type {string|null} */
        this.Entity = init.Entity ?? null;
    }

    /**
     * Builds a human-readable error message string.
     * @returns {string}
     */
    buildErrorMessage() {
        return `Apilane error | Code '${this.Code}' | Message '${this.Message}' | Entity '${this.Entity}' | Property '${this.Property}'`;
    }

    /** @returns {string} */
    toString() {
        return this.buildErrorMessage();
    }
}

// ============================================================================
// Result Type (Either equivalent)
// ============================================================================

/**
 * A discriminated result type that holds either a success value or an ApilaneError.
 * Mirrors the .NET SDK's `Either<TSuccess, ApilaneError>`.
 *
 * @template T
 */
export class ApilaneResult {
    #value;
    #error;
    #isSuccess;

    /**
     * @param {T} [value]
     * @param {ApilaneError} [error]
     */
    constructor(value, error) {
        if (error !== undefined) {
            this.#value = undefined;
            this.#error = error;
            this.#isSuccess = false;
        } else {
            this.#value = value;
            this.#error = undefined;
            this.#isSuccess = true;
        }
    }

    /**
     * Creates a successful result.
     * @template T
     * @param {T} value
     * @returns {ApilaneResult<T>}
     */
    static success(value) {
        return new ApilaneResult(value, undefined);
    }

    /**
     * Creates a failed result.
     * @template T
     * @param {ApilaneError} error
     * @returns {ApilaneResult<T>}
     */
    static error(error) {
        return new ApilaneResult(undefined, error);
    }

    /**
     * Returns true if this result is successful.
     * @returns {boolean}
     */
    get isSuccess() {
        return this.#isSuccess;
    }

    /**
     * Returns true if this result is an error.
     * @returns {boolean}
     */
    get isError() {
        return !this.#isSuccess;
    }

    /**
     * Returns the success value. Throws if the result is an error.
     * @returns {T}
     */
    get value() {
        if (!this.#isSuccess) {
            throw new Error(`Requested invalid value — result is an error: ${this.#error?.buildErrorMessage()}`);
        }
        return this.#value;
    }

    /**
     * Returns the error, or null if the result is successful.
     * @returns {ApilaneError|null}
     */
    get error() {
        return this.#isSuccess ? null : this.#error;
    }

    /**
     * Checks if the result has an error and returns it.
     * @returns {{ hasError: boolean, error: ApilaneError|null }}
     */
    hasError() {
        return { hasError: !this.#isSuccess, error: this.#error ?? null };
    }

    /**
     * Pattern-matches on the result, calling the appropriate callback.
     * @template R
     * @param {function(T): R} onSuccess
     * @param {function(ApilaneError): R} onError
     * @returns {R}
     */
    match(onSuccess, onError) {
        if (typeof onSuccess !== 'function') {
            throw new TypeError('onSuccess must be a function');
        }
        if (typeof onError !== 'function') {
            throw new TypeError('onError must be a function');
        }
        return this.#isSuccess ? onSuccess(this.#value) : onError(this.#error);
    }
}

// ============================================================================
// Filter & Sort Models
// ============================================================================

/**
 * Represents a filter condition or a group of filter conditions.
 * Can be a leaf filter (property + operator + value) or a composite filter (logic + sub-filters).
 */
export class FilterItem {
    /**
     * Creates a leaf filter.
     * @param {string} property - The property name to filter on.
     * @param {string} operator - A FilterOperator value.
     * @param {*} value - The value to compare against.
     * @returns {FilterItem}
     */
    static condition(property, operator, value) {
        const item = new FilterItem();
        item.Property = property;
        item.Operator = operator;
        item.Value = value;
        item.Logic = null;
        item.Filters = null;
        return item;
    }

    /**
     * Creates a composite filter that combines sub-filters with a logical operator.
     * @param {string} logic - A FilterLogic value ('AND' or 'OR').
     * @param {FilterItem[]} filters - The sub-filters to combine.
     * @returns {FilterItem}
     */
    static group(logic, filters) {
        const item = new FilterItem();
        item.Logic = logic;
        item.Filters = filters;
        item.Property = null;
        item.Operator = null;
        item.Value = null;
        return item;
    }

    /**
     * Shorthand for creating an AND group of filters.
     * @param {...FilterItem} filters
     * @returns {FilterItem}
     */
    static and(...filters) {
        return FilterItem.group(FilterLogic.AND, filters);
    }

    /**
     * Shorthand for creating an OR group of filters.
     * @param {...FilterItem} filters
     * @returns {FilterItem}
     */
    static or(...filters) {
        return FilterItem.group(FilterLogic.OR, filters);
    }

    /**
     * Serializes the filter to a plain object for JSON encoding.
     * @returns {object}
     */
    toJSON() {
        if (this.Logic != null) {
            return {
                Logic: this.Logic,
                Filters: this.Filters?.map((f) => f.toJSON()) ?? [],
            };
        }
        return {
            Property: this.Property,
            Operator: this.Operator,
            Value: this.Value,
        };
    }
}

/**
 * Represents a sort specification.
 */
export class SortItem {
    /**
     * @param {string} property - The property name to sort by.
     * @param {string} [direction='asc'] - Sort direction: 'asc' or 'desc'.
     */
    constructor(property, direction = 'asc') {
        /** @type {string} */
        this.Property = property;
        /** @type {string} */
        this.Direction = direction;
    }

    /**
     * Creates an ascending sort.
     * @param {string} property
     * @returns {SortItem}
     */
    static asc(property) {
        return new SortItem(property, 'asc');
    }

    /**
     * Creates a descending sort.
     * @param {string} property
     * @returns {SortItem}
     */
    static desc(property) {
        return new SortItem(property, 'desc');
    }
}

// ============================================================================
// Timestamp Utilities
// ============================================================================

/**
 * Converts a Unix timestamp (seconds or milliseconds) to a Date.
 * @param {number} value
 * @returns {Date|null}
 */
export function unixTimestampToDate(value) {
    if (value == null) {
        return null;
    }
    const str = String(value);
    if (str.length === 10) {
        return new Date(value * 1000);
    }
    if (str.length === 13) {
        return new Date(value);
    }
    return null;
}

/**
 * Converts a Date to a Unix timestamp in seconds.
 * @param {Date} date
 * @returns {number}
 */
export function dateToUnixTimestampSeconds(date) {
    return Math.floor(date.getTime() / 1000);
}

/**
 * Converts a Date to a Unix timestamp in milliseconds.
 * @param {Date} date
 * @returns {number}
 */
export function dateToUnixTimestampMilliseconds(date) {
    return date.getTime();
}

// ============================================================================
// Request Builders
// ============================================================================

/**
 * Base class for all request builders.
 * Provides fluent auth token and error handling configuration.
 *
 * @abstract
 */
class ApilaneRequestBase {
    /** @type {string|null} */
    #controller;
    /** @type {string|null} */
    #action;
    /** @type {string|null} */
    #entity;
    /** @type {string|null} */
    #authToken = null;
    /** @type {boolean} */
    #throwOnError = false;

    /**
     * @param {string|null} entity
     * @param {string} controller
     * @param {string} action
     */
    constructor(entity, controller, action) {
        this.#controller = controller;
        this.#action = action;
        this.#entity = entity;

        if (this.#entity != null && this.#entity.trim().toLowerCase() === 'files') {
            this.#entity = null;
            this.#controller = 'Files';
        }
    }

    /**
     * Sets the authentication token for this request.
     * Overrides any global auth token provider.
     * @param {string} authToken
     * @returns {this}
     */
    withAuthToken(authToken) {
        this.#authToken = authToken;
        return this;
    }

    /**
     * When enabled, the service will throw an Error instead of returning an ApilaneResult with an error.
     * @param {boolean} [throwOnError=true]
     * @returns {this}
     */
    onErrorThrowException(throwOnError = true) {
        this.#throwOnError = throwOnError;
        return this;
    }

    /**
     * @internal
     * @returns {{ hasToken: boolean, token: string|null }}
     */
    _getAuthToken() {
        return { hasToken: this.#authToken != null && this.#authToken.trim() !== '', token: this.#authToken };
    }

    /**
     * @internal
     * @returns {boolean}
     */
    _shouldThrowOnError() {
        return this.#throwOnError;
    }

    /**
     * Returns extra query parameters for this request. Override in subclasses.
     * @internal
     * @returns {URLSearchParams|null}
     */
    _getExtraParams() {
        return null;
    }

    /**
     * Builds the full URL for this request.
     * @param {string} apiUrl - The base API URL.
     * @returns {string}
     */
    getUrl(apiUrl) {
        const params = new URLSearchParams();

        if (this.#entity != null && this.#entity.trim() !== '') {
            params.set('Entity', this.#entity);
        }

        const extra = this._getExtraParams();
        if (extra != null) {
            for (const [key, value] of extra.entries()) {
                params.append(key, value);
            }
        }

        const queryString = params.toString();
        const base = `${apiUrl.replace(/\/+$/, '')}/api/${this.#controller}/${this.#action}`;
        return queryString ? `${base}?${queryString}` : base;
    }
}

// --- Account Requests ---

/** Login request builder. */
export class AccountLoginRequest extends ApilaneRequestBase {
    #loginItem;

    /**
     * @param {{ Email?: string, Username?: string, Password: string }} loginItem
     */
    constructor(loginItem) {
        super(null, 'Account', 'Login');
        this.#loginItem = loginItem;
    }

    /**
     * @param {{ Email?: string, Username?: string, Password: string }} loginItem
     * @returns {AccountLoginRequest}
     */
    static new(loginItem) {
        return new AccountLoginRequest(loginItem);
    }

    /** @internal */
    get _loginItem() {
        return this.#loginItem;
    }
}

/** Register request builder. */
export class AccountRegisterRequest extends ApilaneRequestBase {
    #registerItem;

    /**
     * @param {{ Email: string, Username: string, Password: string, [key: string]: * }} registerItem
     */
    constructor(registerItem) {
        super(null, 'Account', 'Register');
        this.#registerItem = registerItem;
    }

    /**
     * @param {{ Email: string, Username: string, Password: string, [key: string]: * }} registerItem
     * @returns {AccountRegisterRequest}
     */
    static new(registerItem) {
        return new AccountRegisterRequest(registerItem);
    }

    /** @internal */
    get _registerItem() {
        return this.#registerItem;
    }
}

/** Logout request builder. */
export class AccountLogoutRequest extends ApilaneRequestBase {
    #logOutFromEverywhere;

    /**
     * @param {boolean} [logOutFromEverywhere=false]
     */
    constructor(logOutFromEverywhere = false) {
        super(null, 'Account', 'Logout');
        this.#logOutFromEverywhere = logOutFromEverywhere;
    }

    /**
     * @param {boolean} [logOutFromEverywhere=false]
     * @returns {AccountLogoutRequest}
     */
    static new(logOutFromEverywhere = false) {
        return new AccountLogoutRequest(logOutFromEverywhere);
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('everywhere', String(this.#logOutFromEverywhere).toLowerCase());
        return params;
    }
}

/** Renew auth token request builder. */
export class AccountRenewAuthTokenRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Account', 'RenewAuthToken');
    }

    /** @returns {AccountRenewAuthTokenRequest} */
    static new() {
        return new AccountRenewAuthTokenRequest();
    }
}

/** Get user data request builder. */
export class AccountUserDataRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Account', 'UserData');
    }

    /** @returns {AccountUserDataRequest} */
    static new() {
        return new AccountUserDataRequest();
    }
}

/** Account update request builder. */
export class AccountUpdateRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Account', 'Update');
    }

    /** @returns {AccountUpdateRequest} */
    static new() {
        return new AccountUpdateRequest();
    }
}

/** Email confirmation request builder. */
export class AccountConfirmationEmailRequest extends ApilaneRequestBase {
    #email;

    /**
     * @param {string} email
     */
    constructor(email) {
        super(null, 'Email', 'RequestConfirmation');
        this.#email = email;
    }

    /**
     * @param {string} email
     * @returns {AccountConfirmationEmailRequest}
     */
    static new(email) {
        return new AccountConfirmationEmailRequest(email);
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        if (this.#email) {
            params.set('email', this.#email);
        }
        return params;
    }
}

// --- Data Requests ---

/** Get data list request builder with filtering, sorting, and pagination. */
export class DataGetListRequest extends ApilaneRequestBase {
    #pageIndex = 1;
    #pageSize = 20;
    #filter = null;
    #sort = null;
    #properties = null;
    #getTotal = false;

    /**
     * @param {string} entity
     */
    constructor(entity) {
        super(entity, 'Data', 'Get');
    }

    /**
     * @param {string} entity
     * @returns {DataGetListRequest}
     */
    static new(entity) {
        return new DataGetListRequest(entity);
    }

    /**
     * @param {number} pageIndex
     * @returns {this}
     */
    withPageIndex(pageIndex) {
        this.#pageIndex = pageIndex;
        return this;
    }

    /**
     * @param {number} pageSize
     * @returns {this}
     */
    withPageSize(pageSize) {
        this.#pageSize = pageSize;
        return this;
    }

    /**
     * @param {FilterItem} filter
     * @returns {this}
     */
    withFilter(filter) {
        this.#filter = filter;
        return this;
    }

    /**
     * @param {SortItem} sort
     * @returns {this}
     */
    withSort(sort) {
        this.#sort = sort;
        return this;
    }

    /**
     * @param {...string} properties
     * @returns {this}
     */
    withProperties(...properties) {
        this.#properties = properties.flat();
        return this;
    }

    /**
     * @internal
     * @param {boolean} getTotal
     * @returns {this}
     */
    _withTotal(getTotal) {
        this.#getTotal = getTotal;
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('pageIndex', String(this.#pageIndex));
        params.set('pageSize', String(this.#pageSize));
        params.set('getTotal', String(this.#getTotal).toLowerCase());

        if (this.#properties != null && this.#properties.length > 0) {
            params.set('properties', this.#properties.join(','));
        }
        if (this.#filter != null) {
            params.set('filter', JSON.stringify(this.#filter));
        }
        if (this.#sort != null) {
            params.set('sort', JSON.stringify(this.#sort));
        }
        return params;
    }
}

/** Get data by ID request builder. */
export class DataGetByIdRequest extends ApilaneRequestBase {
    #id;
    #properties = null;

    /**
     * @param {string} entity
     * @param {number} id
     */
    constructor(entity, id) {
        super(entity, 'Data', 'GetByID');
        this.#id = id;
    }

    /**
     * @param {string} entity
     * @param {number} id
     * @returns {DataGetByIdRequest}
     */
    static new(entity, id) {
        return new DataGetByIdRequest(entity, id);
    }

    /**
     * @param {...string} properties
     * @returns {this}
     */
    withProperties(...properties) {
        this.#properties = properties.flat();
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('id', String(this.#id));
        if (this.#properties != null && this.#properties.length > 0) {
            params.set('properties', this.#properties.join(','));
        }
        return params;
    }
}

/** Get all data request builder (auto-paginated). */
export class DataGetAllRequest {
    #entity;
    #authToken = null;
    #properties = null;
    #filter = null;
    #sort = null;
    #throwOnError = false;

    /**
     * @param {string} entity
     */
    constructor(entity) {
        this.#entity = entity;
    }

    /**
     * @param {string} entity
     * @returns {DataGetAllRequest}
     */
    static new(entity) {
        return new DataGetAllRequest(entity);
    }

    /** @returns {string} */
    get entity() {
        return this.#entity;
    }

    /** @returns {string|null} */
    get authToken() {
        return this.#authToken;
    }

    /** @returns {string[]|null} */
    get properties() {
        return this.#properties;
    }

    /** @returns {FilterItem|null} */
    get filter() {
        return this.#filter;
    }

    /** @returns {SortItem|null} */
    get sort() {
        return this.#sort;
    }

    /**
     * @param {string} authToken
     * @returns {this}
     */
    withAuthToken(authToken) {
        this.#authToken = authToken;
        return this;
    }

    /**
     * @param {...string} properties
     * @returns {this}
     */
    withProperties(...properties) {
        this.#properties = properties.flat();
        return this;
    }

    /**
     * @param {FilterItem} filter
     * @returns {this}
     */
    withFilter(filter) {
        this.#filter = filter;
        return this;
    }

    /**
     * @param {SortItem} sort
     * @returns {this}
     */
    withSort(sort) {
        this.#sort = sort;
        return this;
    }

    /**
     * @param {boolean} [throwOnError=true]
     * @returns {this}
     */
    onErrorThrowException(throwOnError = true) {
        this.#throwOnError = throwOnError;
        return this;
    }

    /** @internal */
    _shouldThrowOnError() {
        return this.#throwOnError;
    }
}

/** Post data request builder. */
export class DataPostRequest extends ApilaneRequestBase {
    /**
     * @param {string} entity
     */
    constructor(entity) {
        super(entity, 'Data', 'Post');
    }

    /**
     * @param {string} entity
     * @returns {DataPostRequest}
     */
    static new(entity) {
        return new DataPostRequest(entity);
    }
}

/** Put (update) data request builder. */
export class DataPutRequest extends ApilaneRequestBase {
    /**
     * @param {string} entity
     */
    constructor(entity) {
        super(entity, 'Data', 'Put');
    }

    /**
     * @param {string} entity
     * @returns {DataPutRequest}
     */
    static new(entity) {
        return new DataPutRequest(entity);
    }
}

/** Delete data request builder. */
export class DataDeleteRequest extends ApilaneRequestBase {
    #ids = [];

    /**
     * @param {string} entity
     * @param {number[]} [ids]
     */
    constructor(entity, ids) {
        super(entity, 'Data', 'Delete');
        if (ids != null) {
            this.#ids = [...ids];
        }
    }

    /**
     * @param {string} entity
     * @param {number[]} [ids]
     * @returns {DataDeleteRequest}
     */
    static new(entity, ids) {
        return new DataDeleteRequest(entity, ids);
    }

    /**
     * Adds an ID to the list of records to delete.
     * @param {number} id
     * @returns {this}
     */
    addIdToDelete(id) {
        this.#ids.push(id);
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('ids', this.#ids.join(','));
        return params;
    }
}

/** Get application schema request builder. */
export class DataGetSchemaRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Data', 'Schema');
    }

    /** @returns {DataGetSchemaRequest} */
    static new() {
        return new DataGetSchemaRequest();
    }
}

/** Transaction request builder. */
export class DataTransactionRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Data', 'Transaction');
    }

    /** @returns {DataTransactionRequest} */
    static new() {
        return new DataTransactionRequest();
    }
}

/** Transaction operations request builder. */
export class DataTransactionOperationsRequest extends ApilaneRequestBase {
    constructor() {
        super(null, 'Data', 'TransactionOperations');
    }

    /** @returns {DataTransactionOperationsRequest} */
    static new() {
        return new DataTransactionOperationsRequest();
    }
}

// --- Stats Requests ---

/** Stats aggregate request builder. */
export class StatsAggregateRequest extends ApilaneRequestBase {
    #pageIndex = 1;
    #pageSize = 20;
    #filter = null;
    #groupBy = null;
    #ascending = false;
    #properties = [];

    /**
     * @param {string} entity
     */
    constructor(entity) {
        super(entity, 'Stats', 'Aggregate');
    }

    /**
     * @param {string} entity
     * @returns {StatsAggregateRequest}
     */
    static new(entity) {
        return new StatsAggregateRequest(entity);
    }

    /**
     * @param {number} pageIndex
     * @returns {this}
     */
    withPageIndex(pageIndex) {
        this.#pageIndex = pageIndex;
        return this;
    }

    /**
     * @param {number} pageSize
     * @returns {this}
     */
    withPageSize(pageSize) {
        this.#pageSize = pageSize;
        return this;
    }

    /**
     * @param {FilterItem} filter
     * @returns {this}
     */
    withFilter(filter) {
        this.#filter = filter;
        return this;
    }

    /**
     * @param {string} groupBy
     * @returns {this}
     */
    withGroupBy(groupBy) {
        this.#groupBy = groupBy;
        return this;
    }

    /**
     * @param {boolean} ascending
     * @returns {this}
     */
    withSort(ascending) {
        this.#ascending = ascending;
        return this;
    }

    /**
     * Adds a property with an aggregate function to the query.
     * @param {string} property
     * @param {string} aggregate - A DataAggregates value.
     * @returns {this}
     */
    withProperty(property, aggregate) {
        this.#properties.push(`${property}.${aggregate}`);
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        if (this.#groupBy != null) {
            params.set('groupBy', this.#groupBy);
        }
        params.set('pageIndex', String(this.#pageIndex));
        params.set('pageSize', String(this.#pageSize));
        params.set('orderDirection', String(this.#ascending).toLowerCase());

        if (this.#filter != null) {
            params.set('filter', JSON.stringify(this.#filter));
        }
        if (this.#properties.length > 0) {
            params.set('properties', this.#properties.join(','));
        }
        return params;
    }
}

/** Stats distinct request builder. */
export class StatsDistinctRequest extends ApilaneRequestBase {
    #filter = null;
    #property;

    /**
     * @param {string} entity
     * @param {string} property
     */
    constructor(entity, property) {
        super(entity, 'Stats', 'Distinct');
        this.#property = property || 'id';
    }

    /**
     * @param {string} entity
     * @param {string} property
     * @returns {StatsDistinctRequest}
     */
    static new(entity, property) {
        return new StatsDistinctRequest(entity, property);
    }

    /**
     * @param {FilterItem} filter
     * @returns {this}
     */
    withFilter(filter) {
        this.#filter = filter;
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('property', this.#property);
        if (this.#filter != null) {
            params.set('filter', JSON.stringify(this.#filter));
        }
        return params;
    }
}

// --- Custom Endpoint Request ---

/** Custom endpoint request builder. */
export class CustomEndpointRequest extends ApilaneRequestBase {
    #parameters = new Map();

    /**
     * @param {string} endpoint
     * @param {Object<string, number|null>} [parameters]
     */
    constructor(endpoint, parameters) {
        super(null, 'Custom', endpoint);
        if (parameters != null) {
            for (const [key, value] of Object.entries(parameters)) {
                this.#parameters.set(key, value);
            }
        }
    }

    /**
     * @param {string} endpoint
     * @param {Object<string, number|null>} [parameters]
     * @returns {CustomEndpointRequest}
     */
    static new(endpoint, parameters) {
        return new CustomEndpointRequest(endpoint, parameters);
    }

    /**
     * Adds a query parameter to the request.
     * @param {string} key
     * @param {number|null} value
     * @returns {this}
     */
    withParameter(key, value) {
        this.#parameters.set(key, value);
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        for (const [key, value] of this.#parameters.entries()) {
            if (value != null) {
                params.set(key, String(value));
            }
        }
        return params;
    }
}

// --- File Requests ---

/** File list request builder. */
export class FileGetListRequest extends ApilaneRequestBase {
    #pageIndex = 1;
    #pageSize = 20;
    #filter = null;
    #sort = null;
    #properties = null;
    #getTotal = false;

    constructor() {
        super(null, 'Files', 'Get');
    }

    /** @returns {FileGetListRequest} */
    static new() {
        return new FileGetListRequest();
    }

    /**
     * @param {number} pageIndex
     * @returns {this}
     */
    withPageIndex(pageIndex) {
        this.#pageIndex = pageIndex;
        return this;
    }

    /**
     * @param {number} pageSize
     * @returns {this}
     */
    withPageSize(pageSize) {
        this.#pageSize = pageSize;
        return this;
    }

    /**
     * @param {FilterItem} filter
     * @returns {this}
     */
    withFilter(filter) {
        this.#filter = filter;
        return this;
    }

    /**
     * @param {SortItem} sort
     * @returns {this}
     */
    withSort(sort) {
        this.#sort = sort;
        return this;
    }

    /**
     * @param {...string} properties
     * @returns {this}
     */
    withProperties(...properties) {
        this.#properties = properties.flat();
        return this;
    }

    /**
     * @internal
     * @param {boolean} getTotal
     * @returns {this}
     */
    _withTotal(getTotal) {
        this.#getTotal = getTotal;
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('pageIndex', String(this.#pageIndex));
        params.set('pageSize', String(this.#pageSize));
        params.set('getTotal', String(this.#getTotal).toLowerCase());

        if (this.#properties != null && this.#properties.length > 0) {
            params.set('properties', this.#properties.join(','));
        }
        if (this.#filter != null) {
            params.set('filter', JSON.stringify(this.#filter));
        }
        if (this.#sort != null) {
            params.set('sort', JSON.stringify(this.#sort));
        }
        return params;
    }
}

/** File get by ID request builder. */
export class FileGetByIdRequest extends ApilaneRequestBase {
    #id;
    #properties = null;

    /**
     * @param {number} id
     */
    constructor(id) {
        super(null, 'Files', 'GetByID');
        this.#id = id;
    }

    /**
     * @param {number} id
     * @returns {FileGetByIdRequest}
     */
    static new(id) {
        return new FileGetByIdRequest(id);
    }

    /**
     * @param {...string} properties
     * @returns {this}
     */
    withProperties(...properties) {
        this.#properties = properties.flat();
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('id', String(this.#id));
        if (this.#properties != null && this.#properties.length > 0) {
            params.set('properties', this.#properties.join(','));
        }
        return params;
    }
}

/** File upload request builder. */
export class FilePostRequest extends ApilaneRequestBase {
    #fileUid = null;
    #public = false;
    #fileName = null;

    constructor() {
        super(null, 'Files', 'Post');
    }

    /** @returns {FilePostRequest} */
    static new() {
        return new FilePostRequest();
    }

    /**
     * @param {string} fileName
     * @returns {this}
     */
    withFileName(fileName) {
        this.#fileName = fileName;
        return this;
    }

    /**
     * @param {boolean} isPublic
     * @returns {this}
     */
    withPublicFlag(isPublic) {
        this.#public = isPublic;
        return this;
    }

    /**
     * @param {string} fileUid
     * @returns {this}
     */
    withFileUID(fileUid) {
        this.#fileUid = fileUid;
        return this;
    }

    /** @returns {string} */
    getFileName() {
        if (this.#fileName == null || this.#fileName.trim() === '') {
            return crypto.randomUUID().replace(/-/g, '');
        }
        return this.#fileName;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('public', String(this.#public).toLowerCase());
        if (this.#fileUid != null && this.#fileUid.trim() !== '') {
            params.set('uid', this.#fileUid);
        }
        return params;
    }
}

/** File download request builder. */
export class FileDownloadRequest extends ApilaneRequestBase {
    #fileUid = null;
    #fileId = null;

    constructor() {
        super(null, 'Files', 'Download');
    }

    /** @returns {FileDownloadRequest} */
    static new() {
        return new FileDownloadRequest();
    }

    /**
     * @param {number} fileId
     * @returns {this}
     */
    withFileID(fileId) {
        this.#fileId = fileId;
        return this;
    }

    /**
     * @param {string} fileUid
     * @returns {this}
     */
    withFileUID(fileUid) {
        this.#fileUid = fileUid;
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        if (this.#fileId != null) {
            params.set('fileID', String(this.#fileId));
        }
        if (this.#fileUid != null && this.#fileUid.trim() !== '') {
            params.set('fileUID', this.#fileUid);
        }
        return params;
    }
}

/** File delete request builder. */
export class FileDeleteRequest extends ApilaneRequestBase {
    #ids = [];

    /**
     * @param {number[]} [ids]
     */
    constructor(ids) {
        super(null, 'Files', 'Delete');
        if (ids != null) {
            this.#ids = [...ids];
        }
    }

    /**
     * @param {number[]} [ids]
     * @returns {FileDeleteRequest}
     */
    static new(ids) {
        return new FileDeleteRequest(ids);
    }

    /**
     * @param {number} id
     * @returns {this}
     */
    addIdToDelete(id) {
        this.#ids.push(id);
        return this;
    }

    /** @internal */
    _getExtraParams() {
        const params = new URLSearchParams();
        params.set('ids', this.#ids.join(','));
        return params;
    }
}

// ============================================================================
// Transaction Support
// ============================================================================

/**
 * A reference to a prior transaction operation's result.
 * Use `.id()` to get a placeholder string resolved server-side.
 */
export class TransactionRef {
    #operationId;

    /**
     * @param {string} operationId
     */
    constructor(operationId) {
        this.#operationId = operationId;
    }

    /**
     * Returns a placeholder (e.g. "$ref:myOp") that the server resolves
     * to the first ID returned by the referenced operation.
     * @returns {string}
     */
    id() {
        return `$ref:${this.#operationId}`;
    }

    /** @returns {string} */
    toString() {
        return this.id();
    }
}

/**
 * Fluent builder for constructing transaction requests with ordered operations
 * and cross-reference support.
 *
 * @example
 * const { transaction, refs } = new TransactionBuilder()
 *     .post('Orders', { Name: 'Test' })
 *     .post('OrderItems', { OrderId: refs[0].id(), Product: 'Widget' })
 *     .build();
 *
 * @example
 * const builder = new TransactionBuilder();
 * const orderRef = builder.postWithRef('Orders', { Name: 'Test' });
 * builder
 *     .post('OrderItems', { OrderId: orderRef.id(), Product: 'Widget' })
 *     .put('Orders', { ID: orderRef.id(), Status: 'Active' })
 *     .delete('OldOrders', '1,2,3')
 *     .custom('ProcessOrder', { orderId: orderRef.id() });
 * const data = builder.build();
 */
export class TransactionBuilder {
    #operations = [];
    #autoIdCounter = 0;

    /**
     * Adds a Post operation and returns a TransactionRef for cross-referencing.
     * @param {string} entity
     * @param {object} data
     * @returns {TransactionRef}
     */
    postWithRef(entity, data) {
        const operationId = `auto_${this.#autoIdCounter++}`;
        this.#operations.push({
            Action: TransactionAction.Post,
            Entity: entity,
            Id: operationId,
            Data: data,
        });
        return new TransactionRef(operationId);
    }

    /**
     * Adds a Post operation without capturing a reference.
     * @param {string} entity
     * @param {object} data
     * @returns {this}
     */
    post(entity, data) {
        this.postWithRef(entity, data);
        return this;
    }

    /**
     * Adds a Put operation.
     * @param {string} entity
     * @param {object} data - Must include the record ID.
     * @returns {this}
     */
    put(entity, data) {
        this.#operations.push({
            Action: TransactionAction.Put,
            Entity: entity,
            Data: data,
        });
        return this;
    }

    /**
     * Adds a Delete operation.
     * @param {string} entity
     * @param {string} ids - Comma-separated IDs to delete (e.g. "1,2,3").
     * @returns {this}
     */
    delete(entity, ids) {
        this.#operations.push({
            Action: TransactionAction.Delete,
            Entity: entity,
            Ids: ids,
        });
        return this;
    }

    /**
     * Adds a Custom endpoint operation.
     * @param {string} endpointName
     * @param {object} data - Key-value parameters for the endpoint.
     * @returns {this}
     */
    custom(endpointName, data) {
        this.#operations.push({
            Action: TransactionAction.Custom,
            Entity: endpointName,
            Data: data,
        });
        return this;
    }

    /**
     * Builds the transaction operation data.
     * @returns {{ Operations: object[] }}
     */
    build() {
        return {
            Operations: [...this.#operations],
        };
    }
}

// ============================================================================
// Apilane Service
// ============================================================================

/**
 * @callback AuthTokenProvider
 * @returns {Promise<string|null>}
 */

/**
 * The main Apilane service client.
 * Provides methods for all Apilane API operations: Account, Data, Files, Stats, and Custom endpoints.
 */
export class ApilaneService {
    #apiUrl;
    #appToken;
    #authTokenProvider;
    #customFetch;
    #defaultHeaders;

    /**
     * @param {object} config
     * @param {string} config.apiUrl - The Apilane API base URL (e.g. "https://my.api.server").
     * @param {string} config.appToken - The application token.
     * @param {AuthTokenProvider} [config.authTokenProvider] - Optional global auth token provider.
     * @param {typeof fetch} [config.fetch] - Optional custom fetch implementation.
     */
    constructor({ apiUrl, appToken, authTokenProvider, fetch: customFetch } = {}) {
        if (!apiUrl || typeof apiUrl !== 'string' || apiUrl.trim() === '') {
            throw new Error('Apilane api url is required');
        }
        if (!appToken || typeof appToken !== 'string' || appToken.trim() === '') {
            throw new Error('Apilane application token is required');
        }

        this.#apiUrl = apiUrl.replace(/\/+$/, '');
        this.#appToken = appToken;
        this.#authTokenProvider = authTokenProvider ?? null;
        this.#customFetch = customFetch ?? globalThis.fetch.bind(globalThis);
        this.#defaultHeaders = {
            'x-application-token': this.#appToken,
        };
    }

    // ---- Internal helpers ----

    /**
     * Resolves the auth token from the request or the global provider.
     * @param {ApilaneRequestBase} request
     * @returns {Promise<string|null>}
     */
    async #resolveAuthToken(request) {
        const { hasToken, token } = request._getAuthToken();
        if (hasToken) {
            return token;
        }
        if (this.#authTokenProvider != null) {
            return await this.#authTokenProvider();
        }
        return null;
    }

    /**
     * Builds common request headers including auth if available.
     * @param {ApilaneRequestBase} request
     * @returns {Promise<Record<string, string>>}
     */
    async #buildHeaders(request) {
        const headers = { ...this.#defaultHeaders };
        const authToken = await this.#resolveAuthToken(request);
        if (authToken != null && authToken.trim() !== '') {
            headers['Authorization'] = `Bearer ${authToken}`;
        }
        return headers;
    }

    /**
     * Handles the response: parses JSON and returns ApilaneResult or throws.
     * @template T
     * @param {Response} response
     * @param {ApilaneRequestBase} request
     * @param {function(string): T} [transform] - Optional transform for the response body.
     * @returns {Promise<ApilaneResult<T>>}
     */
    async #handleResponse(response, request, transform) {
        const text = await response.text();

        if (!response.ok) {
            let error;
            try {
                const parsed = JSON.parse(text);
                error = new ApilaneError(parsed);
            } catch {
                error = new ApilaneError({ Code: ValidationError.ERROR, Message: text });
            }

            if (request._shouldThrowOnError()) {
                throw new Error(error.buildErrorMessage());
            }
            return ApilaneResult.error(error);
        }

        if (transform != null) {
            return ApilaneResult.success(transform(text));
        }
        try {
            return ApilaneResult.success(JSON.parse(text));
        } catch {
            return ApilaneResult.success(text);
        }
    }

    /**
     * Performs a GET request.
     * @template T
     * @param {ApilaneRequestBase} request
     * @param {AbortSignal} [signal]
     * @param {function(string): T} [transform]
     * @returns {Promise<ApilaneResult<T>>}
     */
    async #get(request, signal, transform) {
        const url = request.getUrl(this.#apiUrl);
        const headers = await this.#buildHeaders(request);
        const response = await this.#customFetch(url, {
            method: 'GET',
            headers,
            signal,
        });
        return this.#handleResponse(response, request, transform);
    }

    /**
     * Performs a POST request with JSON body.
     * @template T
     * @param {ApilaneRequestBase} request
     * @param {*} body
     * @param {AbortSignal} [signal]
     * @param {function(string): T} [transform]
     * @returns {Promise<ApilaneResult<T>>}
     */
    async #postJson(request, body, signal, transform) {
        const url = request.getUrl(this.#apiUrl);
        const headers = await this.#buildHeaders(request);
        headers['Content-Type'] = 'application/json';
        const response = await this.#customFetch(url, {
            method: 'POST',
            headers,
            body: JSON.stringify(body),
            signal,
        });
        return this.#handleResponse(response, request, transform);
    }

    /**
     * Performs a PUT request with JSON body.
     * @template T
     * @param {ApilaneRequestBase} request
     * @param {*} body
     * @param {AbortSignal} [signal]
     * @param {function(string): T} [transform]
     * @returns {Promise<ApilaneResult<T>>}
     */
    async #putJson(request, body, signal, transform) {
        const url = request.getUrl(this.#apiUrl);
        const headers = await this.#buildHeaders(request);
        headers['Content-Type'] = 'application/json';
        const response = await this.#customFetch(url, {
            method: 'PUT',
            headers,
            body: JSON.stringify(body),
            signal,
        });
        return this.#handleResponse(response, request, transform);
    }

    /**
     * Performs a DELETE request.
     * @template T
     * @param {ApilaneRequestBase} request
     * @param {AbortSignal} [signal]
     * @param {function(string): T} [transform]
     * @returns {Promise<ApilaneResult<T>>}
     */
    async #delete(request, signal, transform) {
        const url = request.getUrl(this.#apiUrl);
        const headers = await this.#buildHeaders(request);
        const response = await this.#customFetch(url, {
            method: 'DELETE',
            headers,
            signal,
        });
        return this.#handleResponse(response, request, transform);
    }

    // ========================================================================
    // Health
    // ========================================================================

    /**
     * Checks API liveness.
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number>>}
     */
    async healthCheck(signal) {
        const url = `${this.#apiUrl}/Health/Liveness`;
        const response = await this.#customFetch(url, {
            method: 'GET',
            headers: { ...this.#defaultHeaders },
            signal,
        });
        const text = await response.text();

        if (!response.ok) {
            return ApilaneResult.error(new ApilaneError({
                Code: ValidationError.ERROR,
                Message: text,
            }));
        }
        return ApilaneResult.success(0);
    }

    // ========================================================================
    // Account
    // ========================================================================

    /**
     * Logs in a user and returns the auth token and user data.
     * @param {AccountLoginRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ AuthToken: string, User: object }>>}
     */
    async accountLogin(request, signal) {
        return this.#postJson(request, request._loginItem, signal);
    }

    /**
     * Registers a new user. Returns the new user's ID.
     * @param {AccountRegisterRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number>>}
     */
    async accountRegister(request, signal) {
        return this.#postJson(request, request._registerItem, signal, (text) => {
            const parsed = parseInt(text, 10);
            return isNaN(parsed) ? 0 : parsed;
        });
    }

    /**
     * Logs out the current user.
     * @param {AccountLogoutRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number>>}
     */
    async accountLogout(request, signal) {
        return this.#get(request, signal, (text) => {
            const parsed = parseInt(text, 10);
            return isNaN(parsed) ? 0 : parsed;
        });
    }

    /**
     * Gets the current user's data and security records.
     * @param {AccountUserDataRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ User: object, Security: object[] }>>}
     */
    async getAccountUserData(request, signal) {
        return this.#get(request, signal);
    }

    /**
     * Renews the current auth token.
     * @param {AccountRenewAuthTokenRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<string>>}
     */
    async accountRenewAuthToken(request, signal) {
        return this.#get(request, signal, (text) => text);
    }

    /**
     * Updates the current user's account.
     * @param {AccountUpdateRequest} request
     * @param {object} updateItem - The fields to update.
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<object>>}
     */
    async accountUpdate(request, updateItem, signal) {
        return this.#putJson(request, updateItem, signal);
    }

    // ========================================================================
    // URL Generators
    // ========================================================================

    /**
     * Returns the URL for the forgot password management page.
     * @returns {string}
     */
    urlForAccountManageForgotPassword() {
        return `${this.#apiUrl}/App/${this.#appToken}/Account/Manage/ForgotPassword`;
    }

    /**
     * Returns the URL for requesting email confirmation.
     * @param {string} email
     * @returns {string}
     */
    urlForEmailRequestConfirmation(email) {
        return `${this.#apiUrl}/api/Email/RequestConfirmation?AppToken=${this.#appToken}&Email=${encodeURIComponent(email)}`;
    }

    /**
     * Returns the URL for the forgot password email endpoint.
     * @param {string} email
     * @returns {string}
     */
    urlForEmailForgotPassword(email) {
        return `${this.#apiUrl}/api/Email/ForgotPassword?AppToken=${this.#appToken}&Email=${encodeURIComponent(email)}`;
    }

    // ========================================================================
    // Data
    // ========================================================================

    /**
     * Retrieves the application schema (entities, properties, settings).
     * @param {DataGetSchemaRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<object>>}
     */
    async getApplicationSchema(request, signal) {
        return this.#get(request, signal);
    }

    /**
     * Gets a single record by ID.
     * @param {DataGetByIdRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<object>>}
     */
    async getDataById(request, signal) {
        return this.#get(request, signal);
    }

    /**
     * Gets a paginated list of records.
     * Returns `{ Data: [...] }`.
     * @param {DataGetListRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ Data: object[] }>>}
     */
    async getData(request, signal) {
        request._withTotal(false);
        return this.#get(request, signal);
    }

    /**
     * Gets a paginated list of records including the total count.
     * Returns `{ Data: [...], Total: number }`.
     * @param {DataGetListRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ Data: object[], Total: number }>>}
     */
    async getDataTotal(request, signal) {
        request._withTotal(true);
        return this.#get(request, signal);
    }

    /**
     * Gets all records by auto-paginating through all pages.
     * @param {DataGetAllRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<object[]>>}
     */
    async getAllData(request, signal) {
        const pageSize = 1000;
        let pageIndex = 1;

        const listRequest = DataGetListRequest.new(request.entity)
            .withPageIndex(pageIndex)
            .withPageSize(pageSize)
            .onErrorThrowException(request._shouldThrowOnError());

        listRequest._withTotal(false);

        if (request.authToken != null) {
            listRequest.withAuthToken(request.authToken);
        }
        if (request.filter != null) {
            listRequest.withFilter(request.filter);
        }
        if (request.sort != null) {
            listRequest.withSort(request.sort);
        }
        if (request.properties != null) {
            listRequest.withProperties(...request.properties);
        }

        const allRecords = [];

        const firstPage = await this.getData(listRequest, signal);
        const { hasError, error } = firstPage.hasError();
        if (hasError) {
            return ApilaneResult.error(error);
        }

        allRecords.push(...firstPage.value.Data);

        while (firstPage.value.Data.length >= pageSize) {
            pageIndex++;
            listRequest.withPageIndex(pageIndex);

            const nextPage = await this.getData(listRequest, signal);
            const nextCheck = nextPage.hasError();
            if (nextCheck.hasError) {
                return ApilaneResult.error(nextCheck.error);
            }

            allRecords.push(...nextPage.value.Data);

            if (nextPage.value.Data.length < pageSize) {
                break;
            }
        }

        return ApilaneResult.success(allRecords);
    }

    /**
     * Creates new records. Returns the newly created IDs.
     * @param {DataPostRequest} request
     * @param {object|object[]} data
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number[]>>}
     */
    async postData(request, data, signal) {
        return this.#postJson(request, data, signal);
    }

    /**
     * Updates existing records. Returns the number of affected rows.
     * @param {DataPutRequest} request
     * @param {object} data
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number>>}
     */
    async putData(request, data, signal) {
        return this.#putJson(request, data, signal, (text) => parseInt(text, 10));
    }

    /**
     * Deletes records. Returns the deleted IDs.
     * @param {DataDeleteRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number[]>>}
     */
    async deleteData(request, signal) {
        return this.#delete(request, signal);
    }

    /**
     * Executes a grouped transaction (Post/Put/Delete).
     * @param {DataTransactionRequest} request
     * @param {{ Post?: Array<{ Entity: string, Data: * }>, Put?: Array<{ Entity: string, Data: * }>, Delete?: Array<{ Entity: string, Ids: string }> }} data
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ Post: number[], Put: number, Delete: number[] }>>}
     */
    async transactionData(request, data, signal) {
        return this.#postJson(request, data, signal);
    }

    /**
     * Executes ordered operations inside a transaction scope with cross-reference support.
     * @param {DataTransactionOperationsRequest} request
     * @param {{ Operations: object[] }} data - Built via TransactionBuilder.
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ Results: object[] }>>}
     */
    async transactionOperations(request, data, signal) {
        return this.#postJson(request, data, signal);
    }

    // ========================================================================
    // Files
    // ========================================================================

    /**
     * Gets a paginated list of files.
     * @param {FileGetListRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<{ Data: object[] }>>}
     */
    async getFiles(request, signal) {
        request._withTotal(false);
        return this.#get(request, signal);
    }

    /**
     * Gets a file record by ID.
     * @param {FileGetByIdRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<object>>}
     */
    async getFileById(request, signal) {
        return this.#get(request, signal);
    }

    /**
     * Uploads a file. Returns the new file ID.
     *
     * @param {FilePostRequest} request
     * @param {Blob|ArrayBuffer|Uint8Array} data - The file content.
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number|null>>}
     */
    async postFile(request, data, signal) {
        const url = request.getUrl(this.#apiUrl);
        const headers = await this.#buildHeaders(request);
        // Do not set Content-Type — let fetch set the multipart boundary automatically
        const formData = new FormData();

        let blob;
        if (data instanceof Blob) {
            blob = data;
        } else if (data instanceof ArrayBuffer || data instanceof Uint8Array) {
            blob = new Blob([data]);
        } else {
            throw new TypeError('File data must be a Blob, ArrayBuffer, or Uint8Array');
        }

        formData.append('FileUpload', blob, request.getFileName());

        const response = await this.#customFetch(url, {
            method: 'POST',
            headers,
            body: formData,
            signal,
        });

        return this.#handleResponse(response, request, (text) => {
            const parsed = parseInt(text, 10);
            return isNaN(parsed) ? null : parsed;
        });
    }

    /**
     * Deletes files. Returns the deleted IDs.
     * @param {FileDeleteRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<number[]>>}
     */
    async deleteFile(request, signal) {
        return this.#delete(request, signal);
    }

    // ========================================================================
    // Stats
    // ========================================================================

    /**
     * Retrieves aggregate statistics.
     * @param {StatsAggregateRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<*>>}
     */
    async getStatsAggregate(request, signal) {
        return this.#get(request, signal);
    }

    /**
     * Retrieves distinct values for a property.
     * @param {StatsDistinctRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<*>>}
     */
    async getStatsDistinct(request, signal) {
        return this.#get(request, signal);
    }

    // ========================================================================
    // Custom Endpoints
    // ========================================================================

    /**
     * Calls a custom endpoint and returns the raw JSON response.
     * @param {CustomEndpointRequest} request
     * @param {AbortSignal} [signal]
     * @returns {Promise<ApilaneResult<*>>}
     */
    async getCustomEndpoint(request, signal) {
        return this.#get(request, signal);
    }
}

// ============================================================================
// Factory
// ============================================================================

/**
 * Creates a configured ApilaneService instance.
 *
 * @param {object} config
 * @param {string} config.apiUrl - The Apilane API base URL.
 * @param {string} config.appToken - The application token.
 * @param {AuthTokenProvider} [config.authTokenProvider] - Optional global auth token provider.
 * @param {typeof fetch} [config.fetch] - Optional custom fetch implementation.
 * @returns {ApilaneService}
 *
 * @example
 * import { createApilaneService } from './apilane.js';
 *
 * const apilane = createApilaneService({
 *     apiUrl: 'https://my.api.server',
 *     appToken: 'your-app-token',
 * });
 */
export function createApilaneService(config) {
    return new ApilaneService(config);
}
