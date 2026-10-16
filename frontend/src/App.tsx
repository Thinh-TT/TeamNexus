import { useEffect } from 'react'
import { AppRouter } from './app/router'
import { useAuth } from './features/auth/hooks/useAuth'

function App() {
  const { checkAuth } = useAuth()

  useEffect(() => {
    checkAuth()
  }, [checkAuth])

  return <AppRouter />
}

export default App
