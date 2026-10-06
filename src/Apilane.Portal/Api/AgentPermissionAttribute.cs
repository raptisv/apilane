using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using System;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Classifies an application endpoint for agent access. GET reads, DELETE deletes and other
    /// methods write, unless ReadOnly is set for a read-only POST such as query preview.
    /// Multiple attributes require every listed resource.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class AgentPermissionAttribute : Attribute
    {
        public string Resource { get; }
        public bool ReadOnly { get; set; }

        public AgentPermissionAttribute(string resource)
        {
            Resource = resource;
        }

        public AgentPermissionAccess GetAccess(string method)
        {
            return ReadOnly || HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                ? AgentPermissionAccess.Read
                : HttpMethods.IsDelete(method) ? AgentPermissionAccess.Delete : AgentPermissionAccess.Write;
        }
    }

    /// <summary>
    /// Basic application summaries and access discovery remain available to every collaborator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AgentPermissionDiscoveryAttribute : Attribute
    {
    }
}
