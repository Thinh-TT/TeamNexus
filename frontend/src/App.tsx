import { useEffect } from 'react'
import { ConfigProvider } from 'antd'
import { AppRouter } from './app/router'
import { useAuth } from './features/auth/hooks/useAuth'
import { teamNexusTheme } from './shared/theme/themeConfig'

function App() {
  const { checkAuth } = useAuth()

  useEffect(() => {
    checkAuth()
  }, [checkAuth])

  return (
    <ConfigProvider theme={teamNexusTheme}>
      <AppRouter />
    </ConfigProvider>
  )
}

export default App
