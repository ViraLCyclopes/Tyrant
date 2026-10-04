/** What the mod editor's side list has selected; the page on the right edits it. */
export type Selection = { kind: 'details' } | { kind: 'skin'; id: string } | { kind: 'replace'; texture: string } | { kind: 'check' };
