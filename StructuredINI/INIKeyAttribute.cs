using System;

namespace StructuredINI
{
    [AttributeUsage(AttributeTargets.Property)]
    public class INIKeyAttribute : Attribute
    {
        public string KeyName { get; }

        public INIKeyAttribute(string keyName)
        {
            KeyName = keyName;
        }
    }
}
