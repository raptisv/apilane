using Apilane.Api.Core.Enums;
using Apilane.Common.Utilities;
using System;

namespace Apilane.Api.Core.Exceptions
{
    public class ApilaneException: Exception
    {
        public AppErrors Error;
        public string? CustomMessage, Entity, Property;

        public ApilaneException(
            AppErrors error, 
            string? message = null,
            string? property = null,
            string? entity = null)
        {
            Error = error;
            CustomMessage = message ?? EnumProvider<AppErrors>.GetDisplayValue(error);
            Property = property;
            Entity = entity;
        }
    }
}
