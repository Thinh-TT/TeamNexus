import React from 'react'
import { SearchOutlined } from '@ant-design/icons'
import { Input } from 'antd'

interface TaskSearchBarProps {
  value: string
  onChange: (value: string) => void
  onPressEnter?: () => void
  placeholder?: string
}

export const TaskSearchBar: React.FC<TaskSearchBarProps> = ({
  value,
  onChange,
  onPressEnter,
  placeholder = 'Tìm kiếm theo tiêu đề hoặc mô tả thẻ (tối đa 200 ký tự)...',
}) => {
  return (
    <Input
      size="large"
      prefix={<SearchOutlined style={{ color: '#94a3b8', marginRight: 4 }} />}
      placeholder={placeholder}
      allowClear
      maxLength={200}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      onPressEnter={onPressEnter}
      data-testid="search-input"
      style={{
        borderRadius: 8,
        border: '1px solid #cbd5e1',
        boxShadow: '0 1px 2px rgba(0, 0, 0, 0.05)',
      }}
    />
  )
}
