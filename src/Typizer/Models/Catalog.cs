using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typizer
{
    /// <summary>Схема каталога в списке (<c>GET /v1/catalog</c>).</summary>
    public class CatalogEntryBrief
    {
        /// <summary><c>cat_…</c></summary>
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "catalog_entry";
        /// <summary>Ссылка для <c>Catalog</c> в <see cref="ExtractOptions"/>: <c>schet-faktura</c> или <c>acme/akt-sverki</c>.</summary>
        public string Ref { get; set; } = string.Empty;
        public CatalogKind Kind { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public CatalogCategory Category { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public CatalogPublisherBrief Publisher { get; set; } = new CatalogPublisherBrief();
        public bool Featured { get; set; }
        public CatalogVersionBrief LatestVersion { get; set; } = new CatalogVersionBrief();
        public IReadOnlyList<string> FieldsPreview { get; set; } = Array.Empty<string>();
        public CatalogEntryStats Stats { get; set; } = new CatalogEntryStats();
        public string? WebUrl { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public CatalogViewer Viewer { get; set; } = new CatalogViewer();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "CatalogEntry(" + Ref + ")";
    }

    /// <summary>Схема каталога целиком (<c>GET /v1/catalog/{entry}</c>).</summary>
    public sealed class CatalogEntry : CatalogEntryBrief
    {
        public string? Description { get; set; }
        public string? Prompt { get; set; }
        /// <summary><c>private</c>, <c>unlisted</c>, <c>public</c>.</summary>
        public string Visibility { get; set; } = string.Empty;
        /// <summary>Запрошенная версия со схемой.</summary>
        public CatalogVersion Version { get; set; } = new CatalogVersion();
        public IReadOnlyList<CatalogMajor> Majors { get; set; } = Array.Empty<CatalogMajor>();
        public CatalogLineage? Lineage { get; set; }
        /// <summary>Только для схем вашей организации.</summary>
        public CatalogOwner? Owner { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class CatalogEntryStats
    {
        [JsonPropertyName("orgs_30d")]
        public int? Orgs30d { get; set; }
        public int Favorites { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogViewer
    {
        public bool Favorited { get; set; }
        public IReadOnlyList<CatalogViewerTypizer> Typizers { get; set; } = Array.Empty<CatalogViewerTypizer>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogViewerTypizer
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogMajor
    {
        public int Major { get; set; }
        public string Latest { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogLineage
    {
        public string? EntryId { get; set; }
        public string? Ref { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogOwner
    {
        public string ReviewState { get; set; } = string.Empty;
        public string? ReviewNote { get; set; }
        public bool Hidden { get; set; }
        public string? SourceTypizerId { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Издатель схемы каталога (кратко).</summary>
    public sealed class CatalogPublisherBrief
    {
        public string? Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        /// <summary><c>official</c>, <c>verified</c>, <c>partner</c>.</summary>
        public IReadOnlyList<string> Badges { get; set; } = Array.Empty<string>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Издатель каталога со списком его схем.</summary>
    public sealed class CatalogPublisher
    {
        public string? Id { get; set; }
        public string Object { get; set; } = "catalog_publisher";
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public IReadOnlyList<string> Badges { get; set; } = Array.Empty<string>();
        public string? About { get; set; }
        public string? Website { get; set; }
        public DateTimeOffset Since { get; set; }
        public CatalogPublisherStats Stats { get; set; } = new CatalogPublisherStats();
        public IReadOnlyList<CatalogEntryBrief> Entries { get; set; } = Array.Empty<CatalogEntryBrief>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogPublisherStats
    {
        public int Entries { get; set; }
        [JsonPropertyName("orgs_30d")]
        public int? Orgs30d { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Версия схемы каталога (кратко).</summary>
    public class CatalogVersionBrief
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "catalog_version";
        /// <summary>SemVer: <c>1.2.0</c>.</summary>
        public string Version { get; set; } = string.Empty;
        public int Major { get; set; }
        public bool Breaking { get; set; }
        public string Changelog { get; set; } = string.Empty;
        public int FieldCount { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Версия схемы каталога со схемой и изменениями.</summary>
    public sealed class CatalogVersion : CatalogVersionBrief
    {
        public JsonElement Schema { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public JsonElement? ExampleOutput { get; set; }
        public IReadOnlyList<SchemaChange> Changes { get; set; } = Array.Empty<SchemaChange>();
    }

    /// <summary>Изменение схемы между версиями.</summary>
    public class SchemaChange
    {
        public string Path { get; set; } = string.Empty;
        /// <summary><c>added</c>, <c>removed</c>, <c>type_changed</c>, <c>required_added</c>… (открытый список).</summary>
        public string Kind { get; set; } = string.Empty;
        public bool Breaking { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Отметка «избранное».</summary>
    public sealed class CatalogFavorite
    {
        public string Object { get; set; } = "catalog_favorite";
        public string EntryId { get; set; } = string.Empty;
        public bool Favorited { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Жалоба на схему каталога.</summary>
    public sealed class CatalogReport
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "catalog_report";
        public string Status { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Обновление схемы каталога для типизатора (<c>GET /v1/typizers/{id}/catalog_update</c>).</summary>
    public sealed class CatalogUpdate
    {
        public string Object { get; set; } = "catalog_update";
        public string TypizerId { get; set; } = string.Empty;
        /// <summary><c>installed</c> или <c>forked</c>.</summary>
        public string Relation { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public bool Breaking { get; set; }
        public IReadOnlyList<CatalogChangelogEntry> Changelog { get; set; } = Array.Empty<CatalogChangelogEntry>();
        public IReadOnlyList<CatalogUpdateChange> Changes { get; set; } = Array.Empty<CatalogUpdateChange>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class CatalogChangelogEntry
    {
        public string Version { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Изменение схемы с вашей правкой и конфликтом, если он есть.</summary>
    public sealed class CatalogUpdateChange : SchemaChange
    {
        public JsonElement? Upstream { get; set; }
        public JsonElement? Local { get; set; }
        public bool Conflict { get; set; }
    }
}
