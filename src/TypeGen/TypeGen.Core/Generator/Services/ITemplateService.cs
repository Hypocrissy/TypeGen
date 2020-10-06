using System.Collections.Generic;

namespace TypeGen.Core.Generator.Services
{
    internal interface ITemplateService
    {
        string FillClassTemplate(string imports, string name, string extends, string implements, string properties, string customHead, string customBody, string comment, string fileHeading);
        string FillClassDefaultExportTemplate(string imports, string name, string exportName, string extends, string implements, string properties, string customHead, string customBody, string comment, string fileHeading);
        string FillClassPropertyTemplate(string modifiers, string name, string type, string comment, IEnumerable<string> typeUnions, string defaultValue);
        string FillInterfaceTemplate(string imports, string name, string extends, string properties, string customHead, string customBody, string comment, string fileHeading);
        string FillInterfaceDefaultExportTemplate(string imports, string name, string exportName, string extends, string properties, string customHead, string customBody, string comment, string fileHeading);
        string FillInterfacePropertyTemplate(string modifiers, string name, string type, string comment, IEnumerable<string> typeUnions, bool isOptional);
        string FillEnumTemplate(string imports, string name, string values, bool isConst, string comment, string fileHeading);
        string FillEnumDefaultExportTemplate(string imports, string name, string values, bool isConst, string comment, string fileHeading);
        string FillEnumValueTemplate(string name, object value, string comment);
        string FillImportTemplate(string name, string typeAlias, string path);
        string FillImportDefaultExportTemplate(string name, string path);
        string FillIndexTemplate(string exports);
        string FillIndexExportTemplate(string filename);
        string GetExtendsText(string name);
        string GetExtendsText(IEnumerable<string> names);
        string GetImplementsText(IEnumerable<string> names);
    }
}