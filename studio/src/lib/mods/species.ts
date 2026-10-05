/** The Species tab's key for a dump species id (letters and digits, lower case), to match the two lists. */
export const keyOf = (id: string) => id.toLowerCase().replace(/[^a-z0-9]/g, '');
