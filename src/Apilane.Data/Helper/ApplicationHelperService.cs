using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Data.Abstractions;
using Apilane.Data.Helper.Models;
using Apilane.Data.Repository;
using Apilane.Data.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Transactions;

namespace Apilane.Data.Helper
{
    public class ApplicationHelperService : IApplicationHelperService
    {
        private readonly string _filesPath;

        public ApplicationHelperService(string filesPath)
        {
            _filesPath = filesPath;
        }

        public async Task<List<EmailTemplateDto>> GetEmailsAsync(string appToken, long? templateId)
        {
            var cmd = templateId.HasValue
                ? $"SELECT * FROM [{nameof(H_Email_Templates)}] WHERE ID = {templateId.Value}"
                : $"SELECT * FROM [{nameof(H_Email_Templates)}]";

            var dt = await ExecuteAsync(appToken, cmd);

            return dt.ToDictionary().Select(x => new EmailTemplateDto()
            {
                ID = Utils.GetInt(x[nameof(H_Email_Templates.ID)]),
                EventCode = Utils.GetString(x[nameof(H_Email_Templates.EventCode)]),
                Active = Utils.GetBool(x[nameof(H_Email_Templates.Active)]),
                Subject = Utils.GetString(x[nameof(H_Email_Templates.Subject)]),
                Content = Utils.GetString(x[nameof(H_Email_Templates.Content)])
            }).ToList();
        }

        public async Task<EmailTemplateDto?> GetEmailAsync(string appToken, EmailEventsCodes eventCode)
        {
            var cmd = $"SELECT * FROM [{nameof(H_Email_Templates)}] WHERE [{nameof(H_Email_Templates.EventCode)}] = '{SqlUtilis.GetString(eventCode.ToString())}'";

            var dt = await ExecuteAsync(appToken, cmd);

            return dt.Rows.Count > 0
                ? new EmailTemplateDto()
                {
                    ID = Utils.GetInt(dt.Rows[0][nameof(H_Email_Templates.ID)]),
                    EventCode = Utils.GetString(dt.Rows[0][nameof(H_Email_Templates.EventCode)]),
                    Active = Utils.GetBool(dt.Rows[0][nameof(H_Email_Templates.Active)]),
                    Subject = Utils.GetString(dt.Rows[0][nameof(H_Email_Templates.Subject)]),
                    Content = Utils.GetString(dt.Rows[0][nameof(H_Email_Templates.Content)])
                }
                : null;
        }

        public Task UpdateEmailAsync(string appToken, EmailTemplateDto template)
        {
            var cmd = $@"UPDATE [{nameof(H_Email_Templates)}] SET 
                        [{nameof(H_Email_Templates.Active)}] = '{(template.Active ? 1 : 0)}',
                        [{nameof(H_Email_Templates.Subject)}] = '{SqlUtilis.GetString(template.Subject)}',
                        [{nameof(H_Email_Templates.Content)}] = '{SqlUtilis.GetString(template.Content)}'
                        WHERE [{nameof(H_Email_Templates.ID)}] = {template.ID}";

            return ExecuteAsync(appToken, cmd);
        }

        public async Task<(List<Dictionary<string, object?>> Data, long Total)> GetHistoryForRecordPagedAsync(string appToken, string entity, long recordId, int pageIndex, int pageSize)
        {
            var cmdData = $@"SELECT * FROM [{nameof(H_Entity_Change_Tracking)}]
                         WHERE [{nameof(H_Entity_Change_Tracking.Entity)}] = '{SqlUtilis.GetString(entity)}'
                         AND  [{nameof(H_Entity_Change_Tracking.RecordID)}] = '{recordId}' 
                         ORDER BY [{nameof(H_Entity_Change_Tracking.Created)}] DESC
                         LIMIT {pageSize} OFFSET (({pageIndex} - 1) * {pageSize})";

            var dtData = await ExecuteAsync(appToken, cmdData);

            var cmdTotal = $@"SELECT COUNT(*) FROM [{nameof(H_Entity_Change_Tracking)}] 
                           WHERE [{nameof(H_Entity_Change_Tracking.Entity)}] = '{SqlUtilis.GetString(entity)}'
                           AND  [{nameof(H_Entity_Change_Tracking.RecordID)}] = '{recordId}'";

            var dtTotal = await ExecuteAsync(appToken, cmdTotal);

            return (dtData.ToDictionary(), Utils.GetLong(dtTotal.Rows[0][0], 0));
        }

        public Task DeleteHistoryAsync(string appToken, string entity, List<long>? recordIds)
        {
            var cmdData = $@"DELETE FROM [{nameof(H_Entity_Change_Tracking)}] 
                             WHERE [{nameof(H_Entity_Change_Tracking.Entity)}] = '{SqlUtilis.GetString(entity)}' 
                                {(recordIds is not null && recordIds.Any()
                                ? $" AND [{nameof(H_Entity_Change_Tracking.RecordID)}] IN ({string.Join(",", recordIds)}) "
                                : string.Empty)}";

            return ExecuteAsync(appToken, cmdData);
        }

        public Task CreateHistoryAsync(string appToken, string entity, long recordId, long? userId, Dictionary<string, object?> data)
        {
            var cmdData = $@"INSERT INTO [{nameof(H_Entity_Change_Tracking)}] 
                            (
                                [{nameof(H_Entity_Change_Tracking.Entity)}],
                                [{nameof(H_Entity_Change_Tracking.RecordID)}],
                                [{nameof(H_Entity_Change_Tracking.Owner)}],
                                [{nameof(H_Entity_Change_Tracking.Data)}],
                                [{nameof(H_Entity_Change_Tracking.Created)}]
                            )
                            VALUES
                            (
                                '{SqlUtilis.GetString(entity)}',
                                {recordId},
                                {(userId.HasValue ? userId.Value.ToString() : "null")},
                                '{SqlUtilis.GetString(JsonSerializer.Serialize(data))}',
                                {Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)}
                            );";

            return ExecuteAsync(appToken, cmdData);
        }

        public Task CreateEmailConfirmationTokenAsync(string appToken, long userId, string confirmationToken)
        {
            var cmdData = $@"DELETE FROM [{nameof(H_Auth_Email_Confirmation_Tokens)}] WHERE ({Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)} - [{nameof(H_Auth_Email_Confirmation_Tokens.Created)}]) >= {(1440 * 60 * 1000)};

                            INSERT INTO [{nameof(H_Auth_Email_Confirmation_Tokens)}] 
                            (
                                [{nameof(H_Auth_Email_Confirmation_Tokens.Owner)}],
                                [{nameof(H_Auth_Email_Confirmation_Tokens.Token)}],
                                [{nameof(H_Auth_Email_Confirmation_Tokens.Created)}]
                            )
                            VALUES 
                            (
                                '{userId}',
                                '{SqlUtilis.GetString(confirmationToken)}', 
                                {Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow).ToString()}
                            );";

            return ExecuteAsync(appToken, cmdData);
        }

        public Task CreatePasswordResetTokenAsync(string appToken, long userId, string resetToken)
        {
            var cmdData = $@"DELETE FROM [{nameof(H_Auth_Password_Reset_Tokens)}] WHERE ({Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)} - [{nameof(H_Auth_Password_Reset_Tokens.Created)}]) >= {(1440 * 60 * 1000)};

                            INSERT INTO [{nameof(H_Auth_Password_Reset_Tokens)}] 
                            (
                                [{nameof(H_Auth_Password_Reset_Tokens.Owner)}],
                                [{nameof(H_Auth_Password_Reset_Tokens.Token)}],
                                [{nameof(H_Auth_Password_Reset_Tokens.Created)}]
                            )
                            VALUES 
                            (
                                '{userId}',
                                '{SqlUtilis.GetString(resetToken)}',
                                {Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow).ToString()}
                            );";

            return ExecuteAsync(appToken, cmdData);
        }

        public async Task<long?> GetUserIdFromEmailConfitmationTokenAsync(string appToken, string confirmationToken)
        {
            var cmd = $@"SELECT * FROM [{nameof(H_Auth_Email_Confirmation_Tokens)}] 
                         WHERE [{nameof(H_Auth_Email_Confirmation_Tokens.Token)}] = '{SqlUtilis.GetString(confirmationToken)}' 
                         AND  [{nameof(H_Auth_Email_Confirmation_Tokens.Created)}] >= {Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow.AddHours(-1))}";

            var dt = await ExecuteAsync(appToken, cmd);

            return dt.Rows.Count > 0
                ? Utils.GetNullLong(dt.Rows[0][nameof(H_Auth_Email_Confirmation_Tokens.Owner)])
                : null;
        }

        public async Task<long> GetHistoryCountForEntityAsync(string appToken, string entity)
        {
            var cmd = $@"SELECT COUNT(*) FROM [{nameof(H_Entity_Change_Tracking)}] 
                         WHERE [{nameof(H_Entity_Change_Tracking.Entity)}] = '{entity}'";

            var dt = await ExecuteAsync(appToken, cmd);

            return Utils.GetLong(dt.Rows[0][0], 0);
        }

        public async Task<long?> GetUserIdFromPasswordResetTokenAsync(string appToken, string resetToken)
        {
            var cmd = $@"SELECT * FROM [{nameof(H_Auth_Password_Reset_Tokens)}]
                         WHERE [{nameof(H_Auth_Password_Reset_Tokens.Token)}] = '{SqlUtilis.GetString(resetToken)}'";

            var dt = await ExecuteAsync(appToken, cmd);

            return dt.Rows.Count > 0
                ? Utils.GetNullLong(dt.Rows[0][nameof(H_Auth_Password_Reset_Tokens.Owner)])
                : null;
        }

        public Task DeletePasswordResetTokenAsync(string appToken, string resetToken)
        {
            var cmd = $"DELETE FROM [{nameof(H_Auth_Password_Reset_Tokens)}] WHERE [{nameof(H_Auth_Password_Reset_Tokens.Token)}] = '{SqlUtilis.GetString(resetToken)}'";

            return ExecuteAsync(appToken, cmd);
        }

        private async Task<DataTable> ExecuteAsync(string appToken, string cmd)
        {
            var connSring = await EnsureHelperDatabaseAsync(appToken);

            if (string.IsNullOrWhiteSpace(cmd))
            {
                throw new Exception("Query cannot be emtpy");
            }

            // Suppress any ambient transaction scope to prevent lock between databases.
            using (var transactionScope = new TransactionScope(
                TransactionScopeOption.Suppress,
                new TransactionOptions() { IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted, Timeout = TimeSpan.FromSeconds(5) },
                TransactionScopeAsyncFlowOption.Enabled))
            {
                await using var ctxNoAccess = new SQLiteDataStorageRepository(connSring);
                var result = await ctxNoAccess.ExecTableAsync(cmd);

                transactionScope.Complete();
                return result;
            }
        }

        public async Task<string> EnsureHelperDatabaseAsync(string appToken)
        {
            var directory = appToken.GetRootDirectoryInfo(_filesPath);
            if (!directory.Exists)
            {
                directory.Create();
            }

            var file = new FileInfo(Path.Combine(directory.FullName, $"{appToken}_noaccess.db"));

            var connSring = $"Data Source={file.FullName};Cache Size=2000;Version=3;FailIfMissing=True;";

            if (!file.Exists)
            {
                SQLiteConnection.CreateFile(file.FullName);

                using (var transactionScope = new TransactionScope(TransactionScopeOption.Suppress,
                   new TransactionOptions()
                   {
                       IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted,
                       Timeout = TimeSpan.FromSeconds(5)
                   }, TransactionScopeAsyncFlowOption.Enabled))
                {
                    await using (var ctxNoAccess = new SQLiteDataStorageRepository(connSring))
                    {
                        foreach (var entity in Entities_Helper)
                        {
                            await ctxNoAccess.CreateTableWithPrimaryKeyAsync(entity.Name);

                            foreach (var property in entity.Properties.Where(x => !x.IsPrimaryKey))
                            {
                                await ctxNoAccess.CreateColumnAsync(
                                    entity.Name,
                                    property.Name,
                                    property.TypeID_Enum,
                                    property.Required,
                                    property.DecimalPlaces,
                                    property.Maximum);
                            }
                        }

                        // Create base Email records

                        foreach (var item in EmailEvent.EmailEvents)
                        {
                            await ctxNoAccess.CreateDataAsync(
                                nameof(H_Email_Templates),
                                new Dictionary<string, object?>()
                                {
                                    { nameof(H_Email_Templates.Active), false },
                                    { nameof(H_Email_Templates.Description), item.Description },
                                    { nameof(H_Email_Templates.EventCode), item.Code.ToString() },
                                    { nameof(H_Email_Templates.Subject), item.DefaultSubject },
                                    { nameof(H_Email_Templates.Content), item.DefaultContent }
                                }, false);
                        }
                    }

                    transactionScope.Complete();
                }
            }

            return connSring;
        }

        private static List<DBWS_Entity> Entities_Helper
        {
            get
            {
                return new List<DBWS_Entity>() {
                    new DBWS_Entity()
                    {
                        ID = -1,
                        AppID = -1, // TO FILL
                        IsSystem = true,
                        Name = nameof(H_Entity_Change_Tracking),
                        Description = "Stores the records changes",
                        RequireChangeTracking = false,
                        Properties = new List<DBWS_EntityProperty>()
                        {
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Entity_Change_Tracking.ID),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = true,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Entity_Change_Tracking.Entity),
                                Required = true,
                                Description = "The entity",
                                TypeID = (int)PropertyType.String,
                                Maximum = 100,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Entity_Change_Tracking.RecordID),
                                Required = true,
                                Description = "The record's primary key",
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Entity_Change_Tracking.Owner),
                                Required = false,
                                Description = "The user id that made the change",
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Entity_Change_Tracking.Data),
                                Required = true,
                                Description = "The record's past data",
                                TypeID = (int)PropertyType.String,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = Globals.CreatedColumn,
                                Required = true,
                                Description = "Created (UTC)",
                                ValidationRegex = null,
                                TypeID = (int)PropertyType.Date,
                                Maximum = int.MaxValue,
                                Minimum = 0,
                                IsPrimaryKey = false,
                            }
                        }
                    },
                    new DBWS_Entity()
                    {
                        ID = -1,
                        AppID = -1, // TO FILL
                        IsSystem = true,
                        Name = nameof(H_Auth_Email_Confirmation_Tokens),
                        Description = "Email confirmation tokens",
                        RequireChangeTracking = false,
                        Properties = new List<DBWS_EntityProperty>()
                        {
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Email_Confirmation_Tokens.ID),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = true,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Email_Confirmation_Tokens.Owner),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                DecimalPlaces = 0,
                                Minimum = null,
                                Maximum = null,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Email_Confirmation_Tokens.Token),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.String,
                                Maximum = 100,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = Globals.CreatedColumn,
                                Required = true,
                                Description = "Created (UTC)",
                                ValidationRegex = null,
                                TypeID = (int)PropertyType.Date,
                                Maximum = int.MaxValue,
                                Minimum = 0,
                                IsPrimaryKey = false,
                            }
                        }
                    },
                    new DBWS_Entity()
                    {
                        ID = -1,
                        AppID = -1, // TO FILL
                        IsSystem = true,
                        Name = nameof(H_Auth_Password_Reset_Tokens),
                        Description = "Password reset tokens",
                        RequireChangeTracking = false,
                        Properties = new List<DBWS_EntityProperty>()
                        {
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Password_Reset_Tokens.ID),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = true,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Password_Reset_Tokens.Owner),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                DecimalPlaces = 0,
                                Minimum = null,
                                Maximum = null,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Auth_Password_Reset_Tokens.Token),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.String,
                                Maximum = 100,
                                IsPrimaryKey = false,
                            },
                            new DBWS_EntityProperty()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = Globals.CreatedColumn,
                                Required = true,
                                Description = "Created (UTC)",
                                ValidationRegex = null,
                                TypeID = (int)PropertyType.Date,
                                Maximum = int.MaxValue,
                                Minimum = 0,
                                IsPrimaryKey = false,
                            }
                        }
                    },
                    new DBWS_Entity()
                    {
                        ID = -1,
                        AppID = -1, // TO FILL
                        IsSystem = true,
                        Name = nameof(H_Email_Templates),
                        Description = "Email templates",
                        RequireChangeTracking = false,
                        Properties = new List<DBWS_EntityProperty>()
                        {
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.ID),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Number,
                                IsPrimaryKey = true,
                            },
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.Description),
                                ValidationRegex = "^[a-zA-Z0-9_]+$",
                                Required = true,
                                Description = "The email's reference name",
                                TypeID = (int)PropertyType.String,
                                Minimum = null,
                                Maximum = 100,
                                IsPrimaryKey = false,
                            },
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.Active),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.Boolean,
                                Minimum = null,
                                Maximum = null,
                                IsPrimaryKey = false,
                            },
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.EventCode),
                                Required = true,
                                Description = null,
                                TypeID = (int)PropertyType.String,
                                Minimum = null,
                                Maximum = null,
                                IsPrimaryKey = false,
                            },
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.Subject),
                                Required = true,
                                Description = "The email's subject",
                                TypeID = (int)PropertyType.String,
                                Minimum = null,
                                Maximum = 2000,
                                IsPrimaryKey = false,
                            },
                            new()
                            {
                                ID = -1,
                                IsSystem = true,
                                EntityID = -1,// TO FILL
                                Name = nameof(H_Email_Templates.Content),
                                Required = true,
                                Description = "The email's HTML content",
                                TypeID = (int)PropertyType.String,
                                Minimum = null,
                                Maximum = null,
                                IsPrimaryKey = false,
                            }
                        }
                    }
                };
            }
        }
    }
}
