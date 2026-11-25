import type { ExpoConfig } from 'expo/config'

// Release builds must NOT allow cleartext HTTP. Dev/LAN builds opt in explicitly:
//   PowerShell:  $env:ALLOW_CLEARTEXT='1'; npx expo run:android
// The in-app server-URL validator additionally restricts http:// to private/loopback hosts (src/shared/lib/serverUrl.ts).
const allowCleartext = process.env.ALLOW_CLEARTEXT === '1'

const config: ExpoConfig = {
  name: 'Toobit Platform',
  slug: 'toobit-mobile',
  version: '1.0.0',
  orientation: 'portrait',
  scheme: 'toobit',
  icon: './assets/icon.png',
  userInterfaceStyle: 'automatic',
  android: {
    package: 'com.toobit.platform',
    versionCode: 1,
    adaptiveIcon: {
      backgroundColor: '#0B5CAD',
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
    allowBackup: false, // don't include app data in cloud/ADB backups (the session lives in the Keystore-backed SecureStore)
    predictiveBackGestureEnabled: false,
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    ['expo-localization', { supportsRTL: true }],
    ['expo-build-properties', { android: { usesCleartextTraffic: allowCleartext } }],
  ],
  extra: {
    // Default server for the login screen. 10.0.2.2 is the host machine as seen from the Android emulator.
    defaultApiBaseUrl: process.env.DEFAULT_API_BASE_URL ?? 'http://10.0.2.2:8080',
  },
}

export default config
