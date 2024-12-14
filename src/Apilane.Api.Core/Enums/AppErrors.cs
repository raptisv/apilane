using System.ComponentModel.DataAnnotations;

namespace Apilane.Api.Core.Enums
{
    public enum AppErrors
    {
        [Display(Name = "Something went wrong", Description = "This type of error is caused when a generic error occurs")]
        ERROR,

        [Display(Name = "Unauthorized", Description = "This type of error is caused by an unauthorized call to an endpoint")]
        UNAUTHORIZED,

        [Display(Name = "Unconfirmed email", Description = "This type of error is caused when a user attempts to login, but the email in not confirmed. Applies only if the application is set to disallow unconfirmed email login. Visit Security section for more info")]
        UNCONFIRMED_EMAIL,

        [Display(Name = "Required", Description = "This type of error is caused when a property is required, but missing", AutoGenerateField = true)]
        REQUIRED,

        [Display(Name = "Not found", Description = "This type of error is caused when the desired record is not found")]
        NOT_FOUND,

        [Display(Name = "Invalid filter parameter", Description = "This type of error is caused when the filter parameter is erroneous")]
        INVALID_FILTER_PARAMETER,

        [Display(Name = "Invalid sort parameter", Description = "This type of error is caused when the sort parameter is erroneous")]
        INVALID_SORT_PARAMETER,

        [Display(Name = "Invalid group by parameter", Description = "This type of error is caused when the groupby parameter is erroneous")]
        INVALID_GROUPBY_PARAMETER,

        [Display(Name = "No properties provided", Description = "This type of error is caused when a call to POST or PUT data, does not have any properties")]
        NO_PROPERTIES_PROVIDED,

        [Display(Name = "Empty body", Description = "This type of error is caused when a call to POST or PUT data, does not implement a body")]
        EMPTY_BODY,

        [Display(Name = "No ID provided", Description = "This type of error is caused when a call to PUT data, does not provide the required ID")]
        NO_ID_PROVIDED,

        [Display(Name = "No records found to delete", Description = "This type of error is caused when a call to DELETE data has not found records to delete")]
        NO_RECORDS_FOUND_TO_DELETE,

        [Display(Name = "File not found", Description = "This type of error is caused when a file is not found")]
        FILE_NOT_FOUND,

        [Display(Name = "Validation error", Description = "This type of error is caused when a validation error occurs on a property", AutoGenerateField = true)]
        VALIDATION,

        [Display(Name = "Unique constraint violation", Description = "This type of error is caused when a duplicate value is provided, while the property is marked as Unique", AutoGenerateField = true)]
        UNIQUE_CONSTRAINT_VIOLATION,

        [Display(Name = "Foreign key constraint violation", Description = "This type of error is caused when the value provided for a foreign key property, does not exist on the corresponding entity", AutoGenerateField = true)]
        FOREIGN_KEY_CONSTRAINT_VIOLATION,

        [Display(Name = "Already connected", Description = "This type of error is caused when user tries a connection request to a user, but they already have an active connection")]
        CONNECTION_ALREADY_EXISTS,

        [Display(Name = "There is already a pending request", Description = "This type of error is caused when user tries a connection request to a user, but there is already a request pending")]
        CONNECTION_REQUEST_ALREADY_PENDING,

        [Display(Name = "There is no conection between the two users", Description = "This type of error is caused when user tries to cancel a connection with a user, but they were not connected on the first place")]
        NOT_CONNECTED,

        [Display(Name = "Service unavailable", Description = "This type of error is caused when the owner of the application marks the application as 'Offline'")]
        SERVICE_UNAVAILABLE,

        [Display(Name = "User not found", Description = "This type of error is caused when the user requested was not found")]
        USER_NOT_FOUND,

        [Display(Name = "Too many requests", Description = "This type of error is caused when the user is rate limited")]
        RATE_LIMIT_EXCEEDED
    }
}
