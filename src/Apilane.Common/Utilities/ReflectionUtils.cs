using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Apilane.Common.Utilities
{
    /// <summary>
    /// Collection of Reflection and type conversion related utility functions
    /// </summary>
    public static class ReflectionUtils
    {
        /// <summary>
        /// Binding Flags constant to be reused for all Reflection access methods.
        /// </summary>
        private const BindingFlags MemberAccess =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase;

        private const BindingFlags MemberAccessCom =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        #region Type and Assembly Creation

        /// <summary>
        /// Helper routine that looks up a type name and tries to retrieve the
        /// full type reference using GetType() and if not found looking 
        /// in the actively executing assemblies and optionally loading
        /// the specified assembly name.
        /// </summary>
        /// <param name="typeName">type to load</param>
        /// <param name="assemblyName">
        /// Optional assembly name to load from if type cannot be loaded initially. 
        /// Use for lazy loading of assemblies without taking a type dependency.
        /// </param>
        /// <returns>null</returns>
        private static Type GetTypeFromName(string typeName, string assemblyName)
        {
            var type = Type.GetType(typeName, false);
            if (type != null)
                return type;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            // try to find manually
            foreach (Assembly asm in assemblies)
            {
                type = asm.GetType(typeName, false);

                if (type != null)
                    break;
            }
            if (type != null)
                return type;

            // see if we can load the assembly
            if (!string.IsNullOrEmpty(assemblyName))
            {
                var a = LoadAssembly(assemblyName);
                if (a != null)
                {
                    type = Type.GetType(typeName, false);
                    if (type != null)
                        return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Overload for backwards compatibility which only tries to load
        /// assemblies that are already loaded in memory.
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>        
        private static Type GetTypeFromName(string typeName)
        {
            return GetTypeFromName(typeName, null);
        }

        /// <summary>
        /// Try to load an assembly into the application's app domain.
        /// Loads by name first then checks for filename
        /// </summary>
        /// <param name="assemblyName">Assembly name or full path</param>
        /// <returns>null on failure</returns>
        public static Assembly LoadAssembly(string assemblyName)
        {
            Assembly assembly = null;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch { }

            if (assembly != null)
                return assembly;

            if (File.Exists(assemblyName))
            {
                assembly = Assembly.LoadFrom(assemblyName);
                if (assembly != null)
                    return assembly;
            }
            return null;
        }
        #endregion


        #region Conversions

        /// <summary>
        /// Turns a string into a typed value generically.
        /// Explicitly assigns common types and falls back
        /// on using type converters for unhandled types.         
        /// 
        /// Common uses: 
        /// * UI -&gt; to data conversions
        /// * Parsers
        /// <seealso>Class ReflectionUtils</seealso>
        /// </summary>
        /// <param name="sourceString">
        /// The string to convert from
        /// </param>
        /// <param name="targetType">
        /// The type to convert to
        /// </param>
        /// <param name="culture">
        /// Culture used for numeric and datetime values.
        /// </param>
        /// <returns>object. Throws exception if it cannot be converted.</returns>
        private static object StringToTypedValue(string sourceString, Type targetType, CultureInfo culture = null)
        {
            object result = null;

            bool isEmpty = string.IsNullOrEmpty(sourceString);

            if (culture == null)
                culture = CultureInfo.CurrentCulture;

            if (targetType == typeof(string))
                result = sourceString;
            else if (targetType == typeof(Int32) || targetType == typeof(int))
            {
                if (isEmpty)
                    result = 0;
                else
                    result = Int32.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(Int64))
            {
                if (isEmpty)
                    result = (Int64)0;
                else
                    result = Int64.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(Int16))
            {
                if (isEmpty)
                    result = (Int16)0;
                else
                    result = Int16.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(decimal))
            {
                if (isEmpty)
                    result = 0M;
                else
                    result = decimal.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(DateTime))
            {
                if (isEmpty)
                    result = DateTime.MinValue;
                else
                    result = Convert.ToDateTime(sourceString, culture.DateTimeFormat);
            }
            else if (targetType == typeof(byte))
            {
                if (isEmpty)
                    result = 0;
                else
                    result = Convert.ToByte(sourceString);
            }
            else if (targetType == typeof(double))
            {
                if (isEmpty)
                    result = 0F;
                else
                    result = Double.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(Single))
            {
                if (isEmpty)
                    result = 0F;
                else
                    result = Single.Parse(sourceString, NumberStyles.Any, culture.NumberFormat);
            }
            else if (targetType == typeof(bool))
            {
                sourceString = sourceString.ToLower();
                if (!isEmpty &&
                    sourceString == "true" || sourceString == "on" ||
                    sourceString == "1" || sourceString == "yes")
                    result = true;
                else
                    result = false;
            }
            else if (targetType == typeof(Guid))
            {
                if (isEmpty)
                    result = Guid.Empty;
                else
                    result = new Guid(sourceString);
            }
            else if (targetType.IsEnum)
                result = Enum.Parse(targetType, sourceString);
            else if (targetType == typeof(byte[]))
            {
                // Convert HexBinary string to byte array
                result = null;
            }

            // Handle nullables explicitly since type converter won't handle conversions
            // properly for things like decimal separators currency formats etc.
            // Grab underlying type and pass value to that
            else if (targetType.Name.StartsWith("Nullable`"))
            {
                if (sourceString.ToLower() == "null" || sourceString == string.Empty)
                    result = null;
                else
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                    result = StringToTypedValue(sourceString, targetType);
                }
            }
            else
            {
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                if (converter != null && converter.CanConvertFrom(typeof(string)))
                    result = converter.ConvertFromString(null, culture, sourceString);
                else
                {
                    Debug.Assert(false, string.Format("Type Conversion not handled in StringToTypedValue for {0} {1}",
                        targetType.Name, sourceString));
                    throw (new InvalidCastException("StringToTypedValueValueTypeConversionFailed" + targetType.Name));
                }
            }

            return result;
        }


        #endregion

        #region Member Access

        /// <summary>
        /// Parses Properties and Fields including Array and Collection references.
        /// Used internally for the 'Ex' Reflection methods.
        /// </summary>
        /// <param name="Parent"></param>
        /// <param name="Property"></param>
        /// <returns></returns>
        private static object GetPropertyInternal(object Parent, string Property)
        {
            if (Property == "this" || Property == "me")
            {
                return Parent;
            }

            object result = null;
            string pureProperty = Property;
            string indexes = null;
            bool isArrayOrCollection = false;

            // Deal with Array Property
            if (Property.IndexOf("[") > -1)
            {
                pureProperty = Property.Substring(0, Property.IndexOf("["));
                indexes = Property.Substring(Property.IndexOf("["));
                isArrayOrCollection = true;
            }

            // Get the member
            MemberInfo member = Parent.GetType().GetMember(pureProperty, ReflectionUtils.MemberAccess)[0];
            if (member.MemberType == MemberTypes.Property)
            {
                result = ((PropertyInfo)member).GetValue(Parent, null);
            }
            else
            {
                result = ((FieldInfo)member).GetValue(Parent);
            }

            if (isArrayOrCollection)
            {
                indexes = indexes.Replace("[", string.Empty).Replace("]", string.Empty);

                if (result is Array)
                {
                    int Index = -1;
                    int.TryParse(indexes, out Index);
                    result = CallMethod(result, "GetValue", Index);
                }
                else if (result is ICollection)
                {
                    if (indexes.StartsWith("\""))
                    {
                        // String Index
                        indexes = indexes.Trim('\"');
                        result = CallMethod(result, "get_Item", indexes);
                    }
                    else
                    {
                        // assume numeric index
                        int index = -1;
                        int.TryParse(indexes, out index);
                        result = CallMethod(result, "get_Item", index);
                    }
                }

            }

            return result;
        }


        /// <summary>
        /// Parses Properties and Fields including Array and Collection references.
        /// </summary>
        /// <param name="Parent"></param>
        /// <param name="Property"></param>
        /// <returns></returns>
        private static object SetPropertyInternal(object Parent, string Property, object Value)
        {
            if (Property == "this" || Property == "me")
                return Parent;

            object Result = null;
            string PureProperty = Property;
            string Indexes = null;
            bool IsArrayOrCollection = false;

            // Deal with Array Property
            if (Property.IndexOf("[") > -1)
            {
                PureProperty = Property.Substring(0, Property.IndexOf("["));
                Indexes = Property.Substring(Property.IndexOf("["));
                IsArrayOrCollection = true;
            }

            if (!IsArrayOrCollection)
            {
                // Get the member
                MemberInfo Member = Parent.GetType().GetMember(PureProperty, ReflectionUtils.MemberAccess)[0];
                if (Member.MemberType == MemberTypes.Property)
                    ((PropertyInfo)Member).SetValue(Parent, Value, null);
                else
                    ((FieldInfo)Member).SetValue(Parent, Value);
                return null;
            }
            else
            {
                // Get the member
                MemberInfo Member = Parent.GetType().GetMember(PureProperty, ReflectionUtils.MemberAccess)[0];
                if (Member.MemberType == MemberTypes.Property)
                    Result = ((PropertyInfo)Member).GetValue(Parent, null);
                else
                    Result = ((FieldInfo)Member).GetValue(Parent);
            }
            if (IsArrayOrCollection)
            {
                Indexes = Indexes.Replace("[", string.Empty).Replace("]", string.Empty);

                if (Result is Array)
                {
                    int Index = -1;
                    int.TryParse(Indexes, out Index);
                    Result = CallMethod(Result, "SetValue", Value, Index);
                }
                else if (Result is ICollection)
                {
                    if (Indexes.StartsWith("\""))
                    {
                        // String Index
                        Indexes = Indexes.Trim('\"');
                        Result = CallMethod(Result, "set_Item", Indexes, Value);
                    }
                    else
                    {
                        // assume numeric index
                        int Index = -1;
                        int.TryParse(Indexes, out Index);
                        Result = CallMethod(Result, "set_Item", Index, Value);
                    }
                }
            }

            return Result;
        }

        /// <summary>
        /// Returns a property or field value using a base object and sub members including . syntax.
        /// For example, you can access: oCustomer.oData.Company with (this,"oCustomer.oData.Company")
        /// This method also supports indexers in the Property value such as:
        /// Customer.DataSet.Tables["Customers"].Rows[0]
        /// </summary>
        /// <param name="Parent">Parent object to 'start' parsing from. Typically this will be the Page.</param>
        /// <param name="Property">The property to retrieve. Example: 'Customer.Entity.Company'</param>
        /// <returns></returns>
        private static object GetPropertyEx(object Parent, string Property)
        {
            Type type = Parent.GetType();

            int at = Property.IndexOf(".");
            if (at < 0)
            {
                // Complex parse of the property    
                return GetPropertyInternal(Parent, Property);
            }

            // Walk the . syntax - split into current object (Main) and further parsed objects (Subs)
            string main = Property.Substring(0, at);
            string subs = Property.Substring(at + 1);

            // Retrieve the next . section of the property
            object sub = GetPropertyInternal(Parent, main);

            // Now go parse the left over sections
            return GetPropertyEx(sub, subs);
        }

        /// <summary>
        /// Returns a PropertyInfo object for a given dynamically accessed property
        /// 
        /// Property selection can be specified using . syntax ("Address.Street" or "DataTable[0].Rows[1]") hence the 'Ex' name for this function.
        /// </summary>
        /// <param name="Parent"></param>
        /// <param name="Property"></param>
        /// <returns></returns>
        private static PropertyInfo GetPropertyInfoEx(object Parent, string Property)
        {
            Type type = Parent.GetType();

            int at = Property.IndexOf(".");
            if (at < 0)
            {
                // Complex parse of the property    
                return GetPropertyInfoInternal(Parent, Property);
            }

            // Walk the . syntax - split into current object (Main) and further parsed objects (Subs)
            string main = Property.Substring(0, at);
            string subs = Property.Substring(at + 1);

            // Retrieve the next . section of the property
            object sub = GetPropertyInternal(Parent, main);

            // Now go parse the left over sections
            return GetPropertyInfoEx(sub, subs);
        }

        /// <summary>
        /// Returns a PropertyInfo structure from an extended Property reference
        /// </summary>
        /// <param name="Parent"></param>
        /// <param name="Property"></param>
        /// <returns></returns>
        private static PropertyInfo GetPropertyInfoInternal(object Parent, string Property)
        {
            if (Property == "this" || Property == "me")
                return null;

            string propertyName = Property;

            // Deal with Array Property - strip off array indexer
            if (Property.IndexOf("[") > -1)
                propertyName = Property.Substring(0, Property.IndexOf("["));

            // Get the member
            return Parent.GetType().GetProperty(propertyName, ReflectionUtils.MemberAccess);
        }

        /// <summary>
        /// Sets a value on an object. Supports . syntax for named properties
        /// (ie. Customer.Entity.Company) as well as indexers.
        /// </summary>
        /// <param name="Object Parent">
        /// Object to set the property on.
        /// </param>
        /// <param name="String Property">
        /// Property to set. Can be an object hierarchy with . syntax and can 
        /// include indexers. Examples: Customer.Entity.Company, 
        /// Customer.DataSet.Tables["Customers"].Rows[0]
        /// </param>
        /// <param name="Object Value">
        /// Value to set the property to
        /// </param>
        private static void SetPropertyEx(object parent, string property, object value)
        {
            Type Type = parent.GetType();

            // no more .s - we got our final object
            int lnAt = property.IndexOf(".");
            if (lnAt < 0)
            {
                SetPropertyInternal(parent, property, value);
                return;
            }

            // Walk the . syntax
            string Main = property.Substring(0, lnAt);
            string Subs = property.Substring(lnAt + 1);

            object Sub = GetPropertyInternal(parent, Main);

            SetPropertyEx(Sub, Subs, value);

            return;
        }

        /// <summary>
        /// Calls a method on an object dynamically. This version requires explicit
        /// specification of the parameter type signatures.
        /// </summary>
        /// <param name="instance">Instance of object to call method on</param>
        /// <param name="method">The method to call as a stringToTypedValue</param>
        /// <param name="parameterTypes">Specify each of the types for each parameter passed. 
        /// You can also pass null, but you may get errors for ambiguous methods signatures
        /// when null parameters are passed</param>
        /// <param name="parms">any variable number of parameters.</param>        
        /// <returns>object</returns>
        private static object CallMethod(object instance, string method, Type[] parameterTypes, params object[] parms)
        {
            if (parameterTypes == null && parms.Length > 0)
                // Call without explicit parameter types - might cause problems with overloads    
                // occurs when null parameters were passed and we couldn't figure out the parm type
                return instance.GetType().GetMethod(method, ReflectionUtils.MemberAccess | BindingFlags.InvokeMethod).Invoke(instance, parms);
            else
                // Call with parameter types - works only if no null values were passed
                return instance.GetType().GetMethod(method, ReflectionUtils.MemberAccess | BindingFlags.InvokeMethod, null, parameterTypes, null).Invoke(instance, parms);
        }

        /// <summary>
        /// Calls a method on an object dynamically. 
        /// 
        /// This version doesn't require specific parameter signatures to be passed. 
        /// Instead parameter types are inferred based on types passed. Note that if 
        /// you pass a null parameter, type inferrance cannot occur and if overloads
        /// exist the call may fail. if so use the more detailed overload of this method.
        /// </summary> 
        /// <param name="instance">Instance of object to call method on</param>
        /// <param name="method">The method to call as a stringToTypedValue</param>
        /// <param name="parameterTypes">Specify each of the types for each parameter passed. 
        /// You can also pass null, but you may get errors for ambiguous methods signatures
        /// when null parameters are passed</param>
        /// <param name="parms">any variable number of parameters.</param>        
        /// <returns>object</returns>
        private static object CallMethod(object instance, string method, params object[] parms)
        {
            // Pick up parameter types so we can match the method properly
            Type[] parameterTypes = null;
            if (parms != null)
            {
                parameterTypes = new Type[parms.Length];
                for (int x = 0; x < parms.Length; x++)
                {
                    // if we have null parameters we can't determine parameter types - exit
                    if (parms[x] == null)
                    {
                        parameterTypes = null;  // clear out - don't use types        
                        break;
                    }
                    parameterTypes[x] = parms[x].GetType();
                }
            }
            return CallMethod(instance, method, parameterTypes, parms);
        }

        /// <summary>
        /// Calls a method on an object with extended . syntax (object: this Method: Entity.CalculateOrderTotal)
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="method"></param>
        /// <param name="params"></param>
        /// <returns></returns>
        private static object CallMethodEx(object parent, string method, params object[] parms)
        {
            // no more .s - we got our final object
            int lnAt = method.IndexOf(".");
            if (lnAt < 0)
            {
                return ReflectionUtils.CallMethod(parent, method, parms);
            }

            // Walk the . syntax
            string Main = method.Substring(0, lnAt);
            string Subs = method.Substring(lnAt + 1);

            object Sub = GetPropertyInternal(parent, Main);

            // Recurse until we get the lowest ref
            return CallMethodEx(Sub, Subs, parms);
        }


        /// <summary>
        /// Retrieves a value from  a static property by specifying a type full name and property
        /// </summary>
        /// <param name="typeName">Full type name (namespace.class)</param>
        /// <param name="property">Property to get value from</param>
        /// <returns></returns>
        public static object GetStaticProperty(string typeName, string property)
        {
            Type type = GetTypeFromName(typeName);
            if (type == null)
                return null;

            return GetStaticProperty(type, property);
        }

        /// <summary>
        /// Returns a static property from a given type
        /// </summary>
        /// <param name="type">Type instance for the static property</param>
        /// <param name="property">Property name as a string</param>
        /// <returns></returns>
        private static object GetStaticProperty(Type type, string property)
        {
            try
            {
                return type.InvokeMember(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.GetField | BindingFlags.GetProperty, null, type, null);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region COM Reflection Routines

        /// <summary>
        /// Returns a property or field value using a base object and sub members including . syntax.
        /// For example, you can access: oCustomer.oData.Company with (this,"oCustomer.oData.Company")
        /// </summary>
        /// <param name="parent">Parent object to 'start' parsing from.</param>
        /// <param name="property">The property to retrieve. Example: 'oBus.oData.Company'</param>
        /// <returns></returns>
        private static object GetPropertyExCom(object parent, string property)
        {

            Type Type = parent.GetType();

            int lnAt = property.IndexOf(".");
            if (lnAt < 0)
            {
                if (property == "this" || property == "me")
                    return parent;

                // Get the member
                return parent.GetType().InvokeMember(property, ReflectionUtils.MemberAccessCom | BindingFlags.GetProperty, null,
                    parent, null);
            }

            // Walk the . syntax - split into current object (Main) and further parsed objects (Subs)
            string Main = property.Substring(0, lnAt);
            string Subs = property.Substring(lnAt + 1);

            object Sub = parent.GetType().InvokeMember(Main, ReflectionUtils.MemberAccessCom | BindingFlags.GetProperty, null,
                parent, null);

            // Recurse further into the sub-properties (Subs)
            return ReflectionUtils.GetPropertyExCom(Sub, Subs);
        }

        /// <summary>
        /// Sets the value of a field or property via Reflection. This method alws 
        /// for using '.' syntax to specify objects multiple levels down.
        /// 
        /// ReflectionUtils.SetPropertyEx(this,"Invoice.LineItemsCount",10)
        /// 
        /// which would be equivalent of:
        /// 
        /// Invoice.LineItemsCount = 10;
        /// </summary>
        /// <param name="Object Parent">
        /// Object to set the property on.
        /// </param>
        /// <param name="String Property">
        /// Property to set. Can be an object hierarchy with . syntax.
        /// </param>
        /// <param name="Object Value">
        /// Value to set the property to
        /// </param>
        private static object SetPropertyExCom(object parent, string property, object value)
        {
            Type Type = parent.GetType();

            int lnAt = property.IndexOf(".");
            if (lnAt < 0)
            {
                // Set the member
                parent.GetType().InvokeMember(property, ReflectionUtils.MemberAccessCom | BindingFlags.SetProperty, null,
                    parent, new object[1] { value });

                return null;
            }

            // Walk the . syntax - split into current object (Main) and further parsed objects (Subs)
            string Main = property.Substring(0, lnAt);
            string Subs = property.Substring(lnAt + 1);


            object Sub = parent.GetType().InvokeMember(Main, ReflectionUtils.MemberAccessCom | BindingFlags.GetProperty, null,
                parent, null);

            return SetPropertyExCom(Sub, Subs, value);
        }

        /// <summary>
        /// Wrapper method to call a 'dynamic' (non-typelib) method
        /// on a COM object
        /// </summary>
        /// <param name="params"></param>
        /// <param name="instance"></param>
        /// 1st - Method name, 2nd - 1st parameter, 3rd - 2nd parm etc.
        /// <returns></returns>
        private static object CallMethodCom(object instance, string method, params object[] parms)
        {
            return instance.GetType().InvokeMember(method, ReflectionUtils.MemberAccessCom | BindingFlags.InvokeMethod, null, instance, parms);
        }

        /// <summary>
        /// Calls a method on a COM object with '.' syntax (Customer instance and Address.DoSomeThing method)
        /// </summary>
        /// <param name="parent">the object instance on which to call method</param>
        /// <param name="method">The method or . syntax path to the method (Address.Parse)</param>
        /// <param name="parms">Any number of parameters</param>
        /// <returns></returns>
        private static object CallMethodExCom(object parent, string method, params object[] parms)
        {
            Type Type = parent.GetType();

            // no more .s - we got our final object
            int at = method.IndexOf(".");
            if (at < 0)
            {
                return ReflectionUtils.CallMethodCom(parent, method, parms);
            }

            // Walk the . syntax - split into current object (Main) and further parsed objects (Subs)
            string Main = method.Substring(0, at);
            string Subs = method.Substring(at + 1);

            object Sub = parent.GetType().InvokeMember(Main, ReflectionUtils.MemberAccessCom | BindingFlags.GetProperty, null,
                parent, null);

            // Recurse until we get the lowest ref
            return CallMethodExCom(Sub, Subs, parms);
        }

        #endregion
    }
}
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.