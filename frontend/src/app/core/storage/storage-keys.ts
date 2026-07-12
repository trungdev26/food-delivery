export const STORAGE_KEYS = {
    auth: {
        accessToken: 'auth:access-token',
        refreshToken: 'auth:refresh-token',
        currentUser: 'auth:current-user',
    },

    preferences: {
        theme: 'preferences:theme',
        language: 'preferences:language',
    },

    features: {
        orderFilter: 'features:order-filter',
        selectedBranch: 'features:selected-branch',
    },
} as const;
