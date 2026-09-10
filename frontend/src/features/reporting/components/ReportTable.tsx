import React from 'react'
import { Empty, Table } from 'antd'
import type { TableProps } from 'antd'

export interface ReportTableProps<T> extends TableProps<T> {
  emptyText?: string
}

export function ReportTable<T extends object>({
  emptyText = 'Không có dữ liệu báo cáo',
  locale,
  pagination = { pageSize: 10, showSizeChanger: true },
  ...rest
}: ReportTableProps<T>): React.ReactElement {
  return (
    <Table<T>
      pagination={pagination}
      scroll={{ x: 'max-content' }}
      locale={{
        emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyText} />,
        ...locale,
      }}
      {...rest}
    />
  )
}
