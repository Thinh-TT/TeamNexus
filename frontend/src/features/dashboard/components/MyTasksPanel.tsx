import React from 'react'
import { Card, Empty, List, Tabs } from 'antd'
import type { DashboardResponse } from '../types/dashboard.types'
import { DashboardTaskRow } from './DashboardTaskRow'

interface MyTasksPanelProps {
  workspaceId: string
  myTasks: DashboardResponse['myTasks']
}

export const MyTasksPanel: React.FC<MyTasksPanelProps> = ({ workspaceId, myTasks }) => {
  const { overdue, dueSoon, recentlyAssigned } = myTasks

  const renderTaskList = (
    items: DashboardResponse['myTasks']['overdue']['items'],
    emptyDescription: string
  ) => {
    if (items.length === 0) {
      return (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={emptyDescription}
          style={{ margin: '24px 0' }}
        />
      )
    }

    return (
      <List
        dataSource={items}
        renderItem={(task) => (
          <DashboardTaskRow key={task.id} workspaceId={workspaceId} task={task} />
        )}
      />
    )
  }

  const tabItems = [
    {
      key: 'overdue',
      label: `Quá hạn (${overdue.count})`,
      children: renderTaskList(overdue.items, 'Không có task quá hạn'),
    },
    {
      key: 'dueSoon',
      label: `Sắp đến hạn (${dueSoon.count})`,
      children: renderTaskList(dueSoon.items, 'Không có task sắp đến hạn'),
    },
    {
      key: 'recentlyAssigned',
      label: `Mới giao (${recentlyAssigned.count})`,
      children: renderTaskList(recentlyAssigned.items, 'Không có task mới giao'),
    },
  ]

  return (
    <Card
      title="Task của tôi"
      style={{ borderRadius: 12, border: '1px solid #e2e8f0', height: '100%' }}
      styles={{ body: { padding: '12px 16px' } }}
    >
      <Tabs defaultActiveKey="overdue" items={tabItems} />
    </Card>
  )
}
