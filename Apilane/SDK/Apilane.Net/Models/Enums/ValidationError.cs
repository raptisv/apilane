﻿namespace Apilane.Net.Models.Enums
{
    public enum ValidationError
    {
        ERROR,
        UNAUTHORIZED,
        UNCONFIRMED_EMAIL,
        REQUIRED,
        NOT_FOUND,
        INVALID_FILTER_PARAMETER,
        INVALID_SORT_PARAMETER,
        INVALID_GROUPBY_PARAMETER,
        NO_PROPERTIES_PROVIDED,
        EMPTY_BODY,
        NO_ID_PROVIDED,
        NO_RECORDS_FOUND_TO_DELETE,
        FILE_NOT_FOUND,
        VALIDATION,
        UNIQUE_CONSTRAINT_VIOLATION,
        FOREIGN_KEY_CONSTRAINT_VIOLATION,
        CONNECTION_ALREADY_EXISTS,
        CONNECTION_REQUEST_ALREADY_PENDING,
        NOT_CONNECTED,
        SERVICE_UNAVAILABLE,
        USER_NOT_FOUND,
        RATE_LIMIT_EXCEEDED
    }
}
