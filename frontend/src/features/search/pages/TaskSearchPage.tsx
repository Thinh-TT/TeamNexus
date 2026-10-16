import React, { useMemo } from 'react'
import {
  ArrowLeftOutlined,
  ProjectOutlined,
} from '@ant-design/icons'
import { Button, Flex, Layout, Space, Typography } from 'antd'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { AppHeader } from '../../../shared/components/AppHeader'
import { useTaskSearch } from '../hooks/useTaskSearch'
import { TaskSearchBar } from '../components/TaskSearchBar'
import { TaskSearchFilters } from '../components/TaskSearchFilters'
import { TaskSearchResultList } from '../components/TaskSearchResultList'
import type { TaskSearchFilters as ITaskSearchFilters } from '../types/search.types'

const { Content } = Layout

export const TaskSearchPage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()

  const initialFilters: ITaskSearchFilters = useMemo(() => {
    const q = searchParams.get('q') || undefined
    const boardId = searchParams.get('boardId') || undefined
    const assigneeId = searchParams.get('assigneeId') || undefined
    const unassigned = searchParams.get('unassigned') === 'true'
    const labelIdsStr = searchParams.get('labelIds')
    const labelIds = labelIdsStr ? labelIdsStr.split(',').filter(Boolean) : undefined
    const priority = (searchParams.get('priority') as ITaskSearchFilters['priority']) || undefined
    const dueFrom = searchParams.get('dueFrom') || undefined
    const dueTo = searchParams.get('dueTo') || undefined
    const overdue = searchParams.get('overdue') === 'true'
    const includeDoneParam = searchParams.get('includeDone')
    const includeDone = includeDoneParam !== null ? includeDoneParam === 'true' : true

    return {
      q,
      boardId,
      assigneeId,
      unassigned: unassigned ? true : undefined,
      labelIds,
      priority,
      dueFrom,
      dueTo,
      overdue: overdue ? true : undefined,
      includeDone,
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const {
    filters,
    updateFilters,
    items,
    loading,
    loadingMore,
    hasMore,
    hasQuery,
    loadMore,
    reset,
  } = useTaskSearch(workspaceId, initialFilters)

  const syncToUrl = (newFilters: ITaskSearchFilters) => {
    const params = new URLSearchParams()
    if (newFilters.q) params.set('q', newFilters.q)
    if (newFilters.boardId) params.set('boardId', newFilters.boardId)
    if (newFilters.unassigned) {
      params.set('unassigned', 'true')
    } else if (newFilters.assigneeId) {
      params.set('assigneeId', newFilters.assigneeId)
    }
    if (newFilters.labelIds && newFilters.labelIds.length > 0) {
      params.set('labelIds', newFilters.labelIds.join(','))
    }
    if (newFilters.priority) params.set('priority', newFilters.priority)
    if (newFilters.dueFrom) params.set('dueFrom', newFilters.dueFrom)
    if (newFilters.dueTo) params.set('dueTo', newFilters.dueTo)
    if (newFilters.overdue) params.set('overdue', 'true')
    if (typeof newFilters.includeDone === 'boolean' && !newFilters.includeDone) {
      params.set('includeDone', 'false')
    }
    setSearchParams(params, { replace: true })
  }

  const handleQueryChange = (q: string) => {
    const next = { ...filters, q }
    updateFilters({ q })
    syncToUrl(next)
  }

  const handleFilterChange = (updates: Partial<ITaskSearchFilters>) => {
    const next = { ...filters, ...updates }
    updateFilters(updates)
    syncToUrl(next)
  }

  const handleReset = () => {
    reset()
    setSearchParams(new URLSearchParams(), { replace: true })
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId} />

      <Content style={{ padding: '32px 24px', maxWidth: 1100, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          {/* Header controls */}
          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            <Flex align="center" gap={10}>
              <Button
                type="text"
                icon={<ArrowLeftOutlined />}
                onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
              />
              <div>
                <Flex align="center" gap={8} wrap="wrap">
                  <Typography.Title level={3} style={{ margin: 0, color: '#0f172a', fontWeight: 700, letterSpacing: '-0.02em' }}>
                    Tìm Kiếm & Lọc Thẻ
                  </Typography.Title>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 600,
                      color: '#4f46e5',
                      backgroundColor: '#eef2ff',
                      border: '1px solid #c7d2fe',
                      borderRadius: 999,
                      padding: '2px 9px',
                      fontFamily: "'JetBrains Mono', monospace",
                    }}
                  >
                    Ctrl + K
                  </span>
                </Flex>
                <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                  Tìm kiếm thông minh trên toàn bộ các bảng trong Workspace
                </Typography.Text>
              </div>
            </Flex>

            <Space>
              <Button
                icon={<ProjectOutlined />}
                style={{ borderRadius: 8, fontWeight: 500 }}
                onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
              >
                Danh sách bảng
              </Button>
            </Space>
          </Flex>

          {/* Search bar */}
          <TaskSearchBar value={filters.q || ''} onChange={handleQueryChange} />

          {/* Filter panels */}
          <TaskSearchFilters
            workspaceId={workspaceId}
            filters={filters}
            onFilterChange={handleFilterChange}
            onReset={handleReset}
          />

          {/* Results list */}
          <TaskSearchResultList
            workspaceId={workspaceId}
            items={items}
            loading={loading}
            loadingMore={loadingMore}
            hasMore={hasMore}
            hasQuery={hasQuery}
            onLoadMore={loadMore}
          />
        </Flex>
      </Content>
    </Layout>
  )
}
