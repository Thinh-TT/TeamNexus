import { useEffect } from 'react'
import { ConfigProvider } from 'antd'
import { AppRouter } from './app/router'
import { useAuth } from './features/auth/hooks/useAuth'

const luminousTheme = {
  token: {
    colorPrimary: '#4f46e5',
    colorInfo: '#4f46e5',
    colorSuccess: '#10b981',
    colorWarning: '#f59e0b',
    colorError: '#ef4444',
    colorTextBase: '#0f172a',
    colorBgLayout: '#f8fafc',
    colorBgContainer: '#ffffff',
    colorBorder: '#e2e8f0',
    colorBorderSecondary: '#f1f5f9',
    borderRadius: 8,
    borderRadiusLG: 10,
    borderRadiusSM: 6,
    fontFamily:
      "'Be Vietnam Pro', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    fontSize: 14,
    wireframe: false,
  },
  components: {
    Button: {
      fontWeight: 500,
      controlHeight: 34,
      borderRadius: 8,
    },
    Card: {
      borderRadiusLG: 10,
      colorBorderSecondary: '#e2e8f0',
    },
    Modal: {
      borderRadiusLG: 12,
    },
    Tag: {
      borderRadiusSM: 6,
    },
    Input: {
      borderRadius: 8,
      controlHeight: 34,
    },
    Select: {
      borderRadius: 8,
      controlHeight: 34,
    },
  },
}

function App() {
  const { checkAuth } = useAuth()

  useEffect(() => {
    checkAuth()
  }, [checkAuth])

  return (
    <ConfigProvider theme={luminousTheme}>
      <AppRouter />
    </ConfigProvider>
  )
}

export default App
