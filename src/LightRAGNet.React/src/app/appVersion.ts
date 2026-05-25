const fallbackVersion = '0.1.0';

export const appVersion = import.meta.env.VITE_APP_VERSION?.trim() || fallbackVersion;
