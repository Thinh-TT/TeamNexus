import React, { useState } from 'react'
import {
  FileExcelOutlined,
  FilePdfOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Drawer,
  Flex,
  Radio,
  Space,
  Typography,
} from 'antd'
import { useReportExport } from '../hooks/useReportExport'
import type { ReportFormat } from '../types/reporting.types'

export interface ReportExportDrawerProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  boardId?: string
  from?: string
  to?: string
}

export const ReportExportDrawer: React.FC<ReportExportDrawerProps> = ({
  open,
  onClose,
  workspaceId,
  boardId,
  from,
  to,
}) => {
  const [selectedFormat, setSelectedFormat] = useState<ReportFormat>('pdf')

  const { exporting, error, exportReport } = useReportExport(workspaceId, () => ({
    boardId,
    from,
    to,
  }))

  const handleExport = async (format: ReportFormat) => {
    const success = await exportReport(format)
    if (success) {
      onClose()
    }
  }

  return (
    <Drawer
      title="Xuất Báo Cáo & Dữ Liệu"
      placement="right"
      size="default"
      open={open}
      onClose={onClose}
      styles={{
        body: { padding: 24 },
      }}
      footer={
        <Flex justify="flex-end" gap={12}>
          <Button onClick={onClose} disabled={exporting !== null}>
            Đóng
          </Button>
          <Button
            type="primary"
            icon={selectedFormat === 'pdf' ? <FilePdfOutlined /> : <FileExcelOutlined />}
            loading={exporting === selectedFormat}
            disabled={exporting !== null && exporting !== selectedFormat}
            onClick={() => handleExport(selectedFormat)}
            style={{
              backgroundColor: selectedFormat === 'pdf' ? '#dc2626' : '#16a34a',
              borderColor: selectedFormat === 'pdf' ? '#dc2626' : '#16a34a',
            }}
          >
            {selectedFormat === 'pdf' ? 'Tải Báo Cáo PDF' : 'Tải Báo Cáo Excel'}
          </Button>
        </Flex>
      }
    >
      <Flex vertical gap="large">
        {error && (
          <Alert
            type="error"
            showIcon
            message="Không thể xuất báo cáo"
            description={error}
          />
        )}

        <Typography.Text type="secondary" style={{ fontSize: 13 }}>
          Chọn định dạng tệp bạn muốn xuất. Báo cáo sẽ chứa toàn bộ tiến độ, hiệu suất và hoạt động theo bộ lọc thời gian đang chọn.
        </Typography.Text>

        <Radio.Group
          value={selectedFormat}
          onChange={(e) => setSelectedFormat(e.target.value)}
          style={{ width: '100%' }}
        >
          <Flex vertical gap="middle">
            <Radio value="pdf" style={{ width: '100%' }}>
              <Card
                size="small"
                hoverable
                style={{
                  borderRadius: 8,
                  borderColor: selectedFormat === 'pdf' ? '#dc2626' : '#e2e8f0',
                  backgroundColor: selectedFormat === 'pdf' ? '#fef2f2' : '#ffffff',
                }}
              >
                <Flex align="center" gap={12}>
                  <FilePdfOutlined style={{ fontSize: 24, color: '#dc2626' }} />
                  <div>
                    <Typography.Text strong style={{ display: 'block' }}>
                      Tài liệu PDF (QuestPDF)
                    </Typography.Text>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      Trình bày trang in chuyên nghiệp, bảng biểu trực quan, sẵn sàng để gửi báo cáo hoặc in ấn.
                    </Typography.Text>
                  </div>
                </Flex>
              </Card>
            </Radio>

            <Radio value="excel" style={{ width: '100%' }}>
              <Card
                size="small"
                hoverable
                style={{
                  borderRadius: 8,
                  borderColor: selectedFormat === 'excel' ? '#16a34a' : '#e2e8f0',
                  backgroundColor: selectedFormat === 'excel' ? '#f0fdf4' : '#ffffff',
                }}
              >
                <Flex align="center" gap={12}>
                  <FileExcelOutlined style={{ fontSize: 24, color: '#16a34a' }} />
                  <div>
                    <Typography.Text strong style={{ display: 'block' }}>
                      Bảng tính Excel (ClosedXML)
                    </Typography.Text>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      Gồm 5 sheet chi tiết: Tổng quan, Theo bảng, Theo người, Hoạt động, Sức khoẻ AI kèm AutoFilter và Data Bars.
                    </Typography.Text>
                  </div>
                </Flex>
              </Card>
            </Radio>
          </Flex>
        </Radio.Group>

        <Card
          size="small"
          styles={{ body: { padding: 12 } }}
          style={{
            backgroundColor: '#f8fafc',
            border: '1px dashed #cbd5e1',
            borderRadius: 8,
          }}
        >
          <Space align="start">
            <InfoCircleOutlined style={{ color: '#64748b', marginTop: 3 }} />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              <strong>Bảo mật & On-demand:</strong> Tệp báo cáo được sinh tức thì trong RAM máy chủ và gửi trực tiếp dưới dạng luồng dữ liệu, hoàn toàn <strong>không lưu trữ tệp</strong> trên máy chủ.
            </Typography.Text>
          </Space>
        </Card>
      </Flex>
    </Drawer>
  )
}
