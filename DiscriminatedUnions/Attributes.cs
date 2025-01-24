namespace DiscriminatedUnions {
    // Shamelessly copied from https://gist.github.com/shadeglare/6b46baa340346e575b2751475733405c#file-complete-cs

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UnionTagAttribute : Attribute
    {
        public String TagPropertyName { get; }

        public UnionTagAttribute(String tagPropertyName) => this.TagPropertyName = tagPropertyName;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class UnionCaseAttribute : Attribute
    {
        public Type CaseType { get; }

        public String TagPropertyValue { get; }

        public UnionCaseAttribute(Type caseType, String tagPropertyValue) =>
            (this.CaseType, this.TagPropertyValue) = (caseType, tagPropertyValue);
    }
}