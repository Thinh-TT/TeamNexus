import React from 'react'
import { Flex, Spin, Typography } from 'antd'
import { DeploymentUnitOutlined } from '@ant-design/icons'
import { useAuth } from '../../auth/hooks/useAuth'
import { DashboardPage } from '../../auth/pages/DashboardPage'
import { LandingPage } from './LandingPage'

export const RootHomePage: React.FC = () => {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return (
      <Flex
        align="center"
        justify="center"
        vertical
        gap="middle"
        style={{
          minHeight: '100vh',
          background: '#f8fafc',
          color: '#0f172a',
        }}
      >
        <div
          style={{
            width: 48,
            height: 48,
            borderRadius: 12,
            background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            boxShadow: '0 4px 16px rgba(79, 70, 229, 0.3)',
            marginBottom: 8,
          }}
        >
          <DeploymentUnitOutlined style={{ fontSize: 26, color: '#fff' }} />
        </div>
        <Spin size="large" />
        <Typography.Text style={{ color: '#64748b', fontSize: 14 }}>
          Đang khởi động TeamNexus...
        </Typography.Text>
      </Flex>
    )
  }

  if (isAuthenticated) {
    return <DashboardPage />
  }

  return <LandingPage />
}
