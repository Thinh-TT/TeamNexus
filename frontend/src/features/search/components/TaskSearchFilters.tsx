import React, { useEffect, useState } from 'react'
import { ClearOutlined, FilterOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Col,
  DatePicker,
  Flex,
  Row,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
} from 'antd'
import dayjs from 'dayjs'
import { boardApi } from '../../board/services/boardApi'
import { useWorkspaceMembers } from '../../board/hooks/useWorkspaceMembers'
import type { BoardResponse, LabelResponse } from '../../board/types/board.types'
import type { TaskSearchFilters as ITaskSearchFilters } from '../types/search.types'

const { RangePicker } = DatePicker

interface TaskSearchFiltersProps {
  workspaceId: string
  filters: ITaskSearchFilters
  onFilterChange: (updates: Partial<ITaskSearchFilters>) => void
  onReset: () => void
}

const PRIORITY_OPTIONS = [
  { value: 'Urgent', label: <Tag color="error">Khẩn cấp</Tag> },
  { value: 'High', label: <Tag color="warning">Cao</Tag> },
  { value: 'Medium', label: <Tag color="processing">Trung bình</Tag> },
  { value: 'Low', label: <Tag color="default">Thấp</Tag> },
]

export const TaskSearchFilters: React.FC<TaskSearchFiltersProps> = ({
  workspaceId,
  filters,
  onFilterChange,
  onReset,
}) => {
  const [boards, setBoards] = useState<BoardResponse[]>([])
  const [labels, setLabels] = useState<LabelResponse[]>([])
  const { members } = useWorkspaceMembers(workspaceId)

  useEffect(() => {
    if (!workspaceId) return

    boardApi
      .getBoards(workspaceId)
      .then((data) => setBoards(data))
      .catch(() => {})

    boardApi
      .getLabels(workspaceId)
      .then((data) => setLabels(data))
      .catch(() => {})
  }, [workspaceId])

  // Giá trị cho ô Assignee
  const currentAssigneeValue = filters.unassigned
    ? '__unassigned__'
    : filters.assigneeId || undefined

  return (
    <Card
      size="small"
      style={{ borderRadius: 10, border: '1px solid #e2e8f0', background: '#ffffff' }}
      styles={{ body: { padding: '16px 20px' } }}
    >
      <Flex vertical gap={12}>
        <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
          <Typography.Text strong style={{ fontSize: 14, color: '#334155' }}>
            <FilterOutlined style={{ marginRight: 6 }} />
            Bộ lọc nâng cao
          </Typography.Text>
          <Button
            size="small"
            icon={<ClearOutlined />}
            onClick={onReset}
            data-testid="filter-reset-btn"
          >
            Xoá bộ lọc
          </Button>
        </Flex>

        <Row gutter={[12, 12]}>
          {/* Board */}
          <Col xs={24} sm={12} md={6}>
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
              Bảng Kanban:
            </Typography.Text>
            <Select
              style={{ width: '100%' }}
              allowClear
              placeholder="Tất cả bảng"
              value={filters.boardId || undefined}
              onChange={(val) => onFilterChange({ boardId: val || undefined })}
              options={boards.map((b) => ({ value: b.id, label: b.name }))}
              data-testid="filter-board-select"
            />
          </Col>

          {/* Assignee */}
          <Col xs={24} sm={12} md={6}>
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
              Người thực hiện:
            </Typography.Text>
            <Select
              style={{ width: '100%' }}
              allowClear
              placeholder="Tất cả thành viên"
              value={currentAssigneeValue}
              onChange={(val) => {
                if (val === '__unassigned__') {
                  onFilterChange({ unassigned: true, assigneeId: undefined })
                } else {
                  onFilterChange({ unassigned: undefined, assigneeId: val || undefined })
                }
              }}
              options={[
                { value: '__unassigned__', label: '— Chưa gán —' },
                ...members.map((m) => ({ value: m.userId, label: m.displayName })),
              ]}
              data-testid="filter-assignee-select"
            />
          </Col>

          {/* Priority */}
          <Col xs={24} sm={12} md={6}>
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
              Độ ưu tiên:
            </Typography.Text>
            <Select
              style={{ width: '100%' }}
              allowClear
              placeholder="Tất cả mức độ"
              value={filters.priority || undefined}
              onChange={(val) => onFilterChange({ priority: val || undefined })}
              options={PRIORITY_OPTIONS}
              data-testid="filter-priority-select"
            />
          </Col>

          {/* Labels */}
          <Col xs={24} sm={12} md={6}>
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
              Nhãn (tất cả nhãn đã chọn):
            </Typography.Text>
            <Select
              mode="multiple"
              maxTagCount="responsive"
              style={{ width: '100%' }}
              placeholder="Chọn nhãn..."
              value={filters.labelIds || []}
              onChange={(vals) => {
                // Giới hạn tối đa 10 nhãn
                const capped = vals.slice(0, 10)
                onFilterChange({ labelIds: capped.length > 0 ? capped : undefined })
              }}
              options={labels.map((l) => ({
                value: l.id,
                label: (
                  <Space size={4}>
                    <span
                      style={{
                        display: 'inline-block',
                        width: 8,
                        height: 8,
                        borderRadius: '50%',
                        backgroundColor: l.color,
                      }}
                    />
                    <span>{l.name}</span>
                  </Space>
                ),
              }))}
              data-testid="filter-labels-select"
            />
          </Col>
        </Row>

        <Row gutter={[12, 12]} align="middle">
          {/* Due date range */}
          <Col xs={24} sm={14} md={12}>
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
              Khoảng hạn chót:
            </Typography.Text>
            <RangePicker
              style={{ width: '100%' }}
              format="DD/MM/YYYY"
              value={
                filters.dueFrom && filters.dueTo
                  ? [dayjs(filters.dueFrom), dayjs(filters.dueTo)]
                  : null
              }
              onChange={(dates) => {
                if (dates && dates[0] && dates[1]) {
                  onFilterChange({
                    dueFrom: dates[0].startOf('day').toISOString(),
                    dueTo: dates[1].endOf('day').toISOString(),
                  })
                } else {
                  onFilterChange({ dueFrom: undefined, dueTo: undefined })
                }
              }}
              data-testid="filter-due-range"
            />
          </Col>

          {/* Switches */}
          <Col xs={24} sm={10} md={12}>
            <Flex gap={20} align="center" wrap="wrap" style={{ marginTop: 16 }}>
              <Space>
                <Switch
                  checked={Boolean(filters.overdue)}
                  onChange={(checked) => onFilterChange({ overdue: checked || undefined })}
                  data-testid="filter-overdue-switch"
                />
                <Typography.Text style={{ fontSize: 13 }}>Chỉ task quá hạn</Typography.Text>
              </Space>

              <Space>
                <Switch
                  checked={filters.includeDone !== false}
                  onChange={(checked) => onFilterChange({ includeDone: checked })}
                  data-testid="filter-include-done-switch"
                />
                <Typography.Text style={{ fontSize: 13 }}>Bao gồm task đã xong</Typography.Text>
              </Space>
            </Flex>
          </Col>
        </Row>
      </Flex>
    </Card>
  )
}
