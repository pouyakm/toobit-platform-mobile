# Toobit Mobile (Android, React Native + Expo)

Operator app for the same backend and `contracts/openapi.json` as the web dashboard: Scanner (score breakdown, pin), Positions (confirmed close), Orders (state trail), Signals, Risk & reconciliation (clear pause), Settings (kill switch with typed HALT/RESUME, feature flags). English + Persian with RTL. No exchange credentials ever reach the phone.

> Status: typecheck, lint and 19 unit tests pass. **The Android build and running on an emulator/device were NOT verified** (no Android SDK in the authoring sandbox). Treat your first `expo run:android` as the real check.

## Security model
- 15-minute access token in memory only; single-use rotating refresh token in `expo-secure-store` (Android Keystore). A 401 clears the session.
- Server URL is validated: `https://` anywhere; `http://` only for localhost/private LAN addresses. Release builds disable cleartext traffic entirely (`ALLOW_CLEARTEXT=0`).
- `allowBackup=false`.

## Run on Windows (no Docker)
1. Install Node 22 LTS, JDK 17, Android Studio (SDK + an emulator). Set `ANDROID_HOME`.
2. Start the backend (see root README). Reachable from the emulator at `http://10.0.2.2:8080`; from a real phone use your PC's LAN IP (e.g. `http://192.168.1.20:8080`) and allow it in Windows Firewall.
3. ```powershell
   cd mobile
   npm ci
   $env:ALLOW_CLEARTEXT='1'      # dev only: permits http:// to LAN
   npx expo run:android          # builds a development build and launches it
   ```
4. Enter the server address, email and password on the login screen.

RTL (Persian) needs a development/release build; **Expo Go is not supported**. Switching language restarts the app.

## Build an APK / AAB
```powershell
npm i -g eas-cli; eas login
eas build -p android --profile preview      # APK, https only
eas build -p android --profile production   # AAB for Play
```
Or locally: `npx expo prebuild -p android` then `cd android; .\gradlew assembleRelease` (configure your own signing key; never commit `.jks`).

## Scripts
`npm test`, `npm run typecheck`, `npm run lint`, `npm run gen:api` / `check:api` (regenerate types from the contract).
