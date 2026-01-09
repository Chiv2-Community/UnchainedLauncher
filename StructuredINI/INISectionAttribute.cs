namespace StructuredINI {
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class INISectionAttribute : Attribute {
        public string SectionName { get; }

        public INISectionAttribute(string sectionName) {
            SectionName = sectionName;
        }
    }
}