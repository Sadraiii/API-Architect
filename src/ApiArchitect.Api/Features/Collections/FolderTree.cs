namespace ApiArchitect.Api.Features.Collections;

/// <summary>
/// Checks folder moves against one collection's folders, given as folder id → parent id.
/// Collections hold tens of folders, so walking the map in memory is simpler than a recursive SQL query.
/// </summary>
internal sealed class FolderTree(IReadOnlyDictionary<Guid, Guid?> parents)
{
    /// <summary>
    /// Why <paramref name="folderId"/> (null for a new folder) can't go under <paramref name="newParentId"/>,
    /// or null if it can.
    /// </summary>
    public string? CheckPlacement(Guid? folderId, Guid? newParentId)
    {
        if (newParentId is null)
        {
            return folderId is null || 1 + Height(folderId.Value) <= CollectionRules.MaxFolderDepth
                ? null
                : DepthMessage;
        }

        if (!parents.ContainsKey(newParentId.Value))
        {
            return "The parent folder isn't in this collection.";
        }

        // Walk up from the new parent. Meeting the folder itself means it would end up inside its own subtree.
        var depthOfParent = 0;
        for (Guid? current = newParentId; current is not null; current = parents[current.Value])
        {
            if (current == folderId)
            {
                return "A folder can't be moved into itself or one of its subfolders.";
            }

            depthOfParent++;
        }

        var height = folderId is null ? 1 : Height(folderId.Value);
        return depthOfParent + height <= CollectionRules.MaxFolderDepth ? null : DepthMessage;
    }

    /// <summary>Levels in the subtree rooted at the folder, counting the folder itself.</summary>
    private int Height(Guid folderId)
    {
        var children = parents.Where(p => p.Value == folderId).Select(p => p.Key).ToList();
        return 1 + (children.Count == 0 ? 0 : children.Max(Height));
    }

    private static readonly string DepthMessage = $"Folders can be nested at most {CollectionRules.MaxFolderDepth} levels deep.";
}
