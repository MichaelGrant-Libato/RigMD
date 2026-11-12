interface Group { id: string; count: number; }
interface Selection { id: string; revision: string; }

// Follow new evidence when the active group empties; preserve deliberate tab choices.
export function resolveFindingGroup(groups: Group[], selection: Selection, revision: string): string {
  const selected = groups.find(group => group.id === selection.id);
  if (selected && (selection.revision === revision || selected.count > 0)) return selected.id;
  return groups.find(group => group.count > 0)?.id ?? groups[0]?.id ?? 'attention';
}
