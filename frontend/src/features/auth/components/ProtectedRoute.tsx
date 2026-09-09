import React from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { Flex, Spin } from 'antd'
import { useAuth } from '../hooks/useAuth'

interface ProtectedRouteProps {
  children: React.ReactNode
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children }) => {
  const { isAuthenticated, isLoading } = useAuth()
  const location = useLocation()

  if (isLoading) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100vh', width: '100%' }}>
        <Spin size="large" tip="Đang kiểm tra đăng nhập..." />
      </Flex>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <>{children}</>
}
