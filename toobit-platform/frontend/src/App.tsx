import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { useAuth, refreshTokenStore } from './shared/auth/store'
import { refreshSession } from './shared/api/client'
import { Layout } from './shared/ui/Layout'
import { LoginPage } from './features/auth/LoginPage'
import { ScannerPage } from './features/scanner/ScannerPage'
import { PositionsPage } from './features/positions/PositionsPage'
import { OrdersPage } from './features/orders/OrdersPage'
import { SignalsPage } from './features/signals/SignalsPage'
import { RiskPage } from './features/risk/RiskPage'
import { SettingsPage } from './features/settings/SettingsPage'

export default function App() {
  const token = useAuth((s) => s.accessToken)
  const [booting, setBooting] = useState(() => refreshTokenStore.get() !== null)
  // Restore the session after a reload by rotating the stored refresh token once.
  useEffect(() => { if (booting) void refreshSession().finally(() => setBooting(false)) }, [booting])
  if (booting) return null
  if (!token) return <LoginPage />
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<ScannerPage />} />
          <Route path="positions" element={<PositionsPage />} />
          <Route path="orders" element={<OrdersPage />} />
          <Route path="signals" element={<SignalsPage />} />
          <Route path="risk" element={<RiskPage />} />
          <Route path="settings" element={<SettingsPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
