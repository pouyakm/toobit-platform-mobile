export const en = {
  appName: 'Toobit Platform',
  nav: { scanner: 'Scanner', positions: 'Positions', orders: 'Orders', signals: 'Signals', risk: 'Risk & Logs', settings: 'Settings', more: 'More' },
  common: { loading: 'Loading…', error: 'Something went wrong', confirm: 'Confirm', cancel: 'Cancel', logout: 'Log out', language: 'Language', none: 'Nothing to show yet', retry: 'Retry', restartNote: 'The app will restart to apply the layout direction.' },
  login: { title: 'Sign in', email: 'Email', password: 'Password', submit: 'Sign in', failed: 'Sign-in failed. Check your credentials.', server: 'Server address', serverHint: 'Example: https://trading.example.com or http://192.168.1.20:8080 on your local network.',
    urlInvalid: 'Enter a valid address that starts with http:// or https://', urlScheme: 'Only http:// and https:// are supported.', urlCleartext: 'Plain http:// is only allowed for local network addresses. Use https:// for any other server.', networkError: 'Cannot reach the server. Check the address and your connection.' },
  banner: {
    marketStale: 'Market data is stale. Prices may be out of date.',
    scannerStale: 'Scanner is stale. Showing the last known good ranking.',
    wsDown: 'Live price feed is disconnected. Reconnecting…',
    killOn: 'KILL SWITCH IS ON. Order submission is halted.',
  },
  scanner: {
    title: 'Trending symbols', rank: 'Rank', symbol: 'Symbol', score: 'Score', volume: 'Volume', momentum: 'Momentum', spike: 'Volume spike',
    price: 'Live price', pin: 'Pin', unpin: 'Unpin', why: 'Score explanation', breakdown: 'Per-metric breakdown (normalized 0–1)',
  },
  positions: { title: 'Open positions', symbol: 'Symbol', side: 'Side', qty: 'Quantity', entry: 'Entry', close: 'Close position', closeConfirm: 'Close {symbol} at market? This sends a reduce-only market order and is audited.' },
  orders: { title: 'Orders', symbol: 'Symbol', state: 'State', created: 'Created', trail: 'State trail', retries: 'Unknown retries' },
  signals: { title: 'Signals', action: 'Action', confidence: 'Confidence', model: 'Model', shadow: 'Shadow', features: 'Top contributing features', note: 'Confidence is heuristic, not a calibrated probability.' },
  risk: { title: 'Risk events', recon: 'Reconciliation events', rule: 'Rule', reason: 'Reason', kind: 'Kind', paused: 'Symbol paused', pausedList: 'Symbols paused by reconciliation', clearPause: 'Clear pause' },
  settings: {
    title: 'Strategy & scanner settings', killTitle: 'Kill switch', killOn: 'Halt all order submission', killOff: 'Resume order submission',
    killHint: 'Type {word} to confirm.', reason: 'Reason', flags: 'Feature flags', autoTrading: 'Automated trading', weightSet: 'Scanner weight set', save: 'Save flags',
    readonlyNote: 'Numeric strategy and scanner limits are configuration-file values in this version and are shown read-only.',
  },
}
export type Messages = typeof en
