/** Audio files the game's FMOD plays (Tyrant checks the files' contents, not their names). */
export const AUDIO_EXTENSIONS = ['wav', 'ogg', 'mp3', 'flac'];

/** A sound replacement just added: which game sound, and who hears it. */
export type AddedSound = { event: string; species: string | null; skin: string | null };
