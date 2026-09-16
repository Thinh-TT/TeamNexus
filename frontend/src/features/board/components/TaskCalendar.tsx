import React, { useMemo, useState } from 'react'
import { Button, Calendar, Empty, Flex, Tag, Tooltip, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { CheckCircleFilled, UnorderedListOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import type { ColumnResponse, TaskResponse } from '../types/board.types'
import { dueDateLabel, isOverdue, isTaskCompleted } from '../utils/taskDueDate'
import { getPriorityConfig } from '../utils/taskPriority'
import {
  MAX_TASKS_PER_DAY_CELL,
  groupTasksByDueDate,
  initialCalendarMonth,
  tasksWithoutDueDate,
} from '../utils/boardCalendar'
import { buildBoardSearchUrl, buildDaySearchUrl } from '../utils/calendarNavigation'

export interface TaskCalendarProps {
  workspaceId: string
  boardId: string
  /** Thẻ của board (đã áp bộ lọc tìm kiếm/ưu tiên đang bật trên Kanban). */
  tasks: TaskResponse[]
  /** Cột của board — dùng để biết một thẻ đã nằm ở cột `is_done` hay chưa. */
  columns: ColumnResponse[]
  /** Bấm một thẻ ⇒ mở modal chi tiết hiện có. */
  onTaskClick: (task: TaskResponse) => void
}

/**
 * Xem thẻ theo **chiều thời gian** (Giai đoạn 13 §1) — tab "Lịch" trong `BoardView`.
 *
 * <para>
 * <b>Thuần client, không API mới:</b> nguồn dữ liệu là chính mảng thẻ mà Kanban đang giữ
 * (`useBoard` → `tasksByColumn` → `filteredTasksByColumn`). Nhờ vậy lịch **tôn trọng** ô tìm kiếm và
 * bộ lọc ưu tiên đang bật, thay vì trở thành bộ lọc thứ hai nói chuyện khác.
 * </para>
 *
 * <para>
 * <b>Bấm một ngày ⇒ mở `/search?boardId=&dueFrom=&dueTo=`</b> (đã hoạt động từ Giai đoạn 12). Ô **trống**
 * cũng điều hướng: trang tìm kiếm sẽ hiện "Không tìm thấy kết quả", và đó là câu trả lời đúng — im lặng
 * khi bấm sẽ khiến người dùng tưởng nút hỏng.
 * </para>
 */
export const TaskCalendar: React.FC<TaskCalendarProps> = ({
  workspaceId,
  boardId,
  tasks,
  columns,
  onTaskClick,
}) => {
  const navigate = useNavigate()

  // "Hôm nay" chốt một lần cho mỗi lần render: nhãn quá hạn và ô hôm nay phải nhất quán trong cùng
  // một màn hình, kể cả khi render kéo dài qua nửa đêm.
  const now = useMemo(() => new Date(), [])

  const isDoneColumnByColumnId = useMemo(() => {
    const map = new Map<string, boolean>()
    for (const column of columns) map.set(column.id, column.isDone)
    return map
  }, [columns])

  const days = useMemo(() => groupTasksByDueDate(tasks, now), [tasks, now])
  const withoutDue = useMemo(() => tasksWithoutDueDate(tasks), [tasks])

  /**
   * Tháng đang xem.
   *
   * <para>
   * <b>Phải điều khiển được, và phải suy từ dữ liệu:</b> antd `Calendar` mặc định mở tháng hiện tại của
   * đồng hồ máy. Một board toàn thẻ hạn tháng 6 mà hôm nay là tháng 9 sẽ cho người dùng một lưới **trống**
   * — tính năng trông như hỏng dù dữ liệu vẫn còn. `initialCalendarMonth` chọn tháng gần nhất còn hạn, nên
   * mở tab "Lịch" là thấy việc ngay; ba nút điều hướng vẫn cho phép tới mọi tháng.
   * </para>
   *
   * <para>
   * Cố ý **chỉ** gán `onPanelChange` (đổi tháng) và `onSelect` (chọn ngày ⇒ điều hướng). Không gán
   * `onChange`, vì antd gọi nó cho cả hai việc và như vậy bấm một ngày sẽ vô tình làm lưới nhảy tháng.
   * </para>
   */
  const [viewMonth, setViewMonth] = useState<Dayjs>(() => {
    const initial = initialCalendarMonth(tasks, now)
    return initial ? dayjs(initial, 'YYYY-MM-DD') : dayjs(now)
  })

  const openDay = (day: Dayjs) => {
    navigate(buildDaySearchUrl(workspaceId, boardId, day.format('YYYY-MM-DD')))
  }

  const renderCell = (date: Dayjs) => {
    const summary = days.get(date.format('YYYY-MM-DD'))
    if (!summary) return null

    return (
      <Flex vertical gap={2} style={{ marginTop: 2 }} data-testid={`calendar-cell-${summary.key}`}>
        {summary.items.map((task) => {
          const isDone = isTaskCompleted(task, isDoneColumnByColumnId.get(task.columnId))
          const overdue = isOverdue(task, now, isDoneColumnByColumnId.get(task.columnId))
          const priority = getPriorityConfig(task.priority)

          return (
            <Tooltip
              key={task.id}
              title={`${task.title} · ${dueDateLabel(task, now, isDoneColumnByColumnId.get(task.columnId)) ?? 'Không hạn'}`}
            >
              <div
                data-testid={`calendar-task-${task.id}`}
                onClick={(event) => {
                  // Chặn nổi bọt: bấm thẻ phải mở chi tiết, **không** nhảy sang trang tìm kiếm.
                  event.stopPropagation()
                  onTaskClick(task)
                }}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 4,
                  padding: '1px 4px',
                  borderRadius: 4,
                  fontSize: 11.5,
                  cursor: 'pointer',
                  backgroundColor: overdue ? '#fef2f2' : '#f8fafc',
                  border: `1px solid ${overdue ? '#fecaca' : '#e2e8f0'}`,
                  color: isDone ? '#94a3b8' : '#1e293b',
                  textDecoration: isDone ? 'line-through' : 'none',
                }}
              >
                <span
                  style={{
                    width: 6,
                    height: 6,
                    borderRadius: '50%',
                    flex: '0 0 6px',
                    backgroundColor: overdue ? '#ef4444' : priority?.bg === '#f8fafc' ? '#94a3b8' : '#6366f1',
                  }}
                />
                <span
                  style={{
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {task.title}
                </span>
                {isDone && <CheckCircleFilled style={{ color: '#10b981', fontSize: 10 }} />}
              </div>
            </Tooltip>
          )
        })}

        {summary.overflowCount > 0 && (
          <Typography.Text
            type="secondary"
            style={{ fontSize: 11, paddingLeft: 4 }}
            data-testid={`calendar-overflow-${summary.key}`}
          >
            +{summary.overflowCount} thẻ khác
          </Typography.Text>
        )}

        {summary.hasOverdue && (
          <Tag color="error" style={{ margin: 0, fontSize: 10, lineHeight: '14px' }}>
            Có thẻ quá hạn
          </Tag>
        )}
      </Flex>
    )
  }

  if (tasks.length === 0) {
    return (
      <Flex align="center" justify="center" style={{ flex: 1, padding: 48 }}>
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="Bảng này chưa có thẻ nào để hiển thị trên lịch."
        />
      </Flex>
    )
  }

  return (
    <Flex vertical style={{ flex: 1, overflowY: 'auto', padding: '16px 24px' }} data-testid="task-calendar">
      <Flex justify="space-between" align="center" wrap="wrap" gap={8} style={{ marginBottom: 8 }}>
        <Typography.Text type="secondary" style={{ fontSize: 12.5 }}>
          Thẻ được xếp theo <strong>hạn chót</strong>. Bấm một ngày để mở danh sách đầy đủ trong trang
          tìm kiếm.
        </Typography.Text>

        {withoutDue.length > 0 && (
          <Button
            size="small"
            type="link"
            icon={<UnorderedListOutlined />}
            onClick={() => navigate(buildBoardSearchUrl(workspaceId, boardId))}
            data-testid="calendar-without-due-btn"
            style={{ fontSize: 12.5, padding: 0 }}
          >
            {withoutDue.length} thẻ không có hạn chót
          </Button>
        )}
      </Flex>

      <Calendar
        // Tháng đang xem do component quyết định (xem `viewMonth`) — không để antd tự lấy tháng hiện tại.
        value={viewMonth}
        onPanelChange={(date) => setViewMonth(date)}
        // Mỗi ô chỉ chứa tối đa `MAX_TASKS_PER_DAY_CELL` thẻ; phần còn lại gộp thành `+N`.
        cellRender={(date, info) => (info.type === 'date' ? renderCell(date) : null)}
        onSelect={(date, info) => {
          // antd gọi `onSelect` với `source !== 'date'` khi người dùng bấm vào tiêu đề tháng/năm (nó coi
          // đó là "chọn" ngày đầu tháng). Chỉ điều hướng khi thực sự bấm một ô ngày.
          if (info.source !== 'date') return
          openDay(date)
        }}
        headerRender={({ value, onChange }) => (
          <Flex
            justify="space-between"
            align="center"
            wrap="wrap"
            gap={8}
            style={{ padding: '8px 4px' }}
          >
            <Typography.Title level={5} style={{ margin: 0 }}>
              Lịch thẻ theo hạn chót
            </Typography.Title>

            <Flex align="center" gap={6}>
              <Button size="small" onClick={() => onChange(value.subtract(1, 'month'))}>
                Tháng trước
              </Button>
              <Button size="small" onClick={() => onChange(dayjs())}>
                Hôm nay
              </Button>
              <Button size="small" onClick={() => onChange(value.add(1, 'month'))}>
                Tháng sau
              </Button>
            </Flex>
          </Flex>
        )}
      />
    </Flex>
  )
}

/** Số thẻ tối đa mỗi ô — xuất lại để test và `BoardView` không phải biết hằng số nằm ở đâu. */
export { MAX_TASKS_PER_DAY_CELL }
