using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Spt.Templates;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Json;

namespace PLATE.Server.Tests;

/// <summary>
/// The services reach the database through <see cref="DatabaseServer"/>, which the
/// server fills in during startup. GetTables is virtual, so a test says what is in the
/// database by answering the question rather than by staging a server.
/// </summary>
public sealed class TestDatabaseServer(DatabaseTables tables) : DatabaseServer
{
    public override DatabaseTables GetTables() => tables;

    /// <summary>
    /// A database holding nothing but the items, and the locale strings when a test
    /// needs them. Nearly every property of these records is required and none of the
    /// rest is ever read, hence the uninitialized objects: filling them in would mean
    /// inventing a whole server's worth of data to reach one dictionary.
    /// </summary>
    public static TestDatabaseServer With(Dictionary<MongoId, TemplateItem> items,
        IReadOnlyDictionary<string, string>? locale = null)
    {
        var templates = (Templates)RuntimeHelpers.GetUninitializedObject(typeof(Templates));
        typeof(Templates).GetProperty(nameof(Templates.Items))!.SetValue(templates, items);

        var tables = (DatabaseTables)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseTables));
        typeof(DatabaseTables).GetProperty(nameof(DatabaseTables.Templates))!.SetValue(tables, templates);

        if (locale != null)
        {
            var locales = (LocaleBase)RuntimeHelpers.GetUninitializedObject(typeof(LocaleBase));
            var global = new Dictionary<string, LazyLoad<Dictionary<string, string>>>
            {
                ["en"] = new(() => locale.ToDictionary(p => p.Key, p => p.Value)),
            };
            typeof(LocaleBase).GetProperty(nameof(LocaleBase.Global))!.SetValue(locales, global);
            typeof(DatabaseTables).GetProperty(nameof(DatabaseTables.Locales))!.SetValue(tables, locales);
        }

        return new TestDatabaseServer(tables);
    }
}
