import React, { useEffect, useState } from 'react'
import { DatePicker, Flex, Select } from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { reportingApi } from '../services/reportingApi'
import type { ReportBoardOptionResponse } from '../types/reporting.types'

const { RangePicker } = DatePicker

export interface ReportFiltersProps {
  workspaceId: string
  selectedBoardId?: string
  from?: string
  to?: string
  onChange: (params: { boardId?: string; from?: string; to?: string }) => void
  loading?: boolean
}

type PresetDays = '7' | '30' | '90' | 'custom'

export const ReportFilters: React.FC<ReportFiltersProps> = ({
  workspaceId,
  selectedBoardId,
  from,
  to,
  onChange,
  loading = false,
}) => {
  const [boards, setBoards] = useState<ReportBoardOptionResponse[]>([])
  const [preset, setPreset] = useState<PresetDays>('30')

  useEffect(() => {
    let ignore = false
    reportingApi
      .listReportBoards(workspaceId)
      .then((data) => {
        if (!ignore) {
          setBoards(data)
        }
      })
      .catch(() => {})

    return () => {
      ignore = true
    }
  }, [workspaceId])

  const handlePresetChange = (val: PresetDays) => {
    setPreset(val)
    if (val === 'custom') return

    const days = parseInt(val, 10)
    const newTo = dayjs().toISOString()
    const newFrom = dayjs().subtract(days, 'day').toISOString()

    onChange({
      boardId: selectedBoardId,
      from: newFrom,
      to: newTo,
    })
  }

  const handleRangeChange = (dates: [Dayjs | null, Dayjs | null] | null) => {
    if (!dates || !dates[0] || !dates[1]) {
      setPreset('30')
      const newTo = dayjs().toISOString()
      const newFrom = dayjs().subtract(30, 'day').toISOString()
      onChange({ boardId: selectedBoardId, from: newFrom, to: newTo })
      return
    }

    setPreset('custom')
    onChange({
      boardId: selectedBoardId,
      from: dates[0].startOf('day').toISOString(),
      to: dates[1].endOf('day').toISOString(),
    })
  }

  const handleBoardChange = (value: string | undefined) => {
    onChange({
      boardId: value,
      from,
      to,
    })
  }

  const rangeValue: [Dayjs | null, Dayjs | null] | null =
    from && to ? [dayjs(from), dayjs(to)] : null

  return (
    <Flex wrap="wrap" gap="middle" align="center">
      {/* Board Selector */}
      <Select
        placeholder="Chọn phạm vi Bảng"
        value={selectedBoardId || undefined}
        onChange={handleBoardChange}
        allowClear
        loading={loading}
        style={{ minWidth: 220 }}
        options={[
          { value: '', label: '📊 Toàn bộ Workspace' },
          ...boards.map((b) => ({
            value: b.id,
            label: `📋 ${b.name} (${b.taskCount} task)`,
          })),
        ]}
      />

      {/* Preset range */}
      <Select
        value={preset}
        onChange={handlePresetChange}
        style={{ width: 140 }}
        options={[
          { value: '7', label: '7 ngày qua' },
          { value: '30', label: '30 ngày qua' },
          { value: '90', label: '90 ngày qua' },
          { value: 'custom', label: 'Tuỳ chỉnh...' },
        ]}
      />

      {/* Date Range Picker */}
      <RangePicker
        value={rangeValue}
        onChange={handleRangeChange}
        format="DD/MM/YYYY"
        allowClear={false}
        style={{ width: 260 }}
      />
    </Flex>
  )
}
