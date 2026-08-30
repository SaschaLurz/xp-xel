using System.Text.Json.Nodes;

namespace XPXel.Formats.Project;

/// <summary>
/// Upgrades a project manifest from one schema version to the next.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists before it is needed.</b> There are no migrations yet — the format is
/// at version 1. The mechanism is in place from the first release because the alternative is
/// discovering, at version 2, that a year of users' files cannot be opened. Establishing the
/// contract now costs a few lines; retro-fitting it costs a support burden.
/// </para>
/// <para>
/// <b>Writing one.</b> Migrations operate on the JSON document tree rather than on the
/// strongly-typed <see cref="ProjectManifest"/>, because by definition the old file does not
/// match the current class. Each migration handles exactly one version step, and the
/// pipeline chains them, so upgrading a version 1 file to version 4 runs three small,
/// individually testable migrations rather than one sprawling one.
/// </para>
/// <example>
/// A migration that renamed a property would look like:
/// <code>
/// public sealed class RenameOpacityMigration : IProjectMigration
/// {
///     public int FromVersion =&gt; 1;
///
///     public void Migrate(JsonObject manifest)
///     {
///         foreach (JsonObject layer in EnumerateLayers(manifest))
///         {
///             if (layer.Remove("alpha", out JsonNode? value))
///             {
///                 layer["opacity"] = value;
///             }
///         }
///     }
/// }
/// </code>
/// </example>
/// </remarks>
internal interface IProjectMigration
{
    /// <summary>
    /// The schema version this migration reads. It always produces
    /// <c>FromVersion + 1</c>.
    /// </summary>
    int FromVersion { get; }

    /// <summary>Upgrades the manifest in place.</summary>
    /// <param name="manifest">The parsed manifest, modified directly.</param>
    void Migrate(JsonObject manifest);
}

/// <summary>
/// Applies whatever chain of migrations is needed to bring a manifest up to
/// <see cref="ProjectSchema.CurrentVersion"/>.
/// </summary>
internal static class ProjectMigrationPipeline
{
    /// <summary>
    /// The registered migrations. Add new ones here in version order; the pipeline sorts
    /// them regardless, but keeping the list ordered makes the format's history readable.
    /// </summary>
    private static readonly IProjectMigration[] Migrations = [];

    /// <summary>Upgrades a manifest to the current schema version.</summary>
    /// <param name="manifest">The parsed manifest, modified in place.</param>
    /// <exception cref="ImageFormatException">
    /// The file was written by a newer version of XP-xel, or by a version whose upgrade path
    /// is missing.
    /// </exception>
    public static void Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        int version = manifest.TryGetPropertyValue("schemaVersion", out JsonNode? node) && node is not null
            ? node.GetValue<int>()
            : 1;

        if (version > ProjectSchema.CurrentVersion)
        {
            throw new ImageFormatException(
                $"This project was saved by a newer version of XP-xel (format version {version}; " +
                $"this build understands up to {ProjectSchema.CurrentVersion}). Please update XP-xel to open it.");
        }

        while (version < ProjectSchema.CurrentVersion)
        {
            IProjectMigration? migration = Array.Find(Migrations, m => m.FromVersion == version);
            if (migration is null)
            {
                throw new ImageFormatException(
                    $"This project uses format version {version}, and no upgrade path to version " +
                    $"{ProjectSchema.CurrentVersion} is available in this build.");
            }

            migration.Migrate(manifest);
            version = migration.FromVersion + 1;
            manifest["schemaVersion"] = version;
        }
    }
}
