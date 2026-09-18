import React from 'react'
import { Flex, Spin, Typography } from 'antd'
import { NexusLogo } from '../../../shared/components/NexusLogo'
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
        <NexusLogo size={56} style={{ marginBottom: 8 }} />
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
