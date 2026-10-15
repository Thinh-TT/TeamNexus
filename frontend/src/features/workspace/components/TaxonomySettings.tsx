import React, { useCallback, useEffect, useState } from 'react'
import {
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  TagsOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Flex,
  Input,
  Space,
  Spin,
  Tag,
  Typography,
  message,
} from 'antd'
import { jevApi } from '../../ai/services/jevApi'
import type { TaxonomyKind } from '../../ai/types/jev.types'

interface LocalTaxonomyItem {
  id?: string
  kind: TaxonomyKind
  key: string
  label: string
  colorHex?: string | null
  position?: number
}

interface TaxonomySettingsProps {
  workspaceId: string
  isManagerOrAdmin: boolean
}

const DEFAULT_DOMAINS = [
  { key: 'Backend', label: 'Backend', colorHex: '#3b82f6' },
  { key: 'Frontend', label: 'Frontend', colorHex: '#10b981' },
  { key: 'DevOps', label: 'DevOps', colorHex: '#f59e0b' },
  { key: 'Database', label: 'Database', colorHex: '#8b5cf6' },
  { key: 'Mobile', label: 'Mobile', colorHex: '#ec4899' },
  { key: 'UI/UX', label: 'UI/UX', colorHex: '#06b6d4' },
  { key: 'QA/Testing', label: 'QA/Testing', colorHex: '#14b8a6' },
]

const DEFAULT_WORK_TYPES = [
  { key: 'Feature', label: 'Feature', colorHex: '#3b82f6' },
  { key: 'Bug', label: 'Bug', colorHex: '#ef4444' },
  { key: 'Refactor', label: 'Refactor', colorHex: '#8b5cf6' },
  { key: 'TechDebt', label: 'TechDebt', colorHex: '#f97316' },
  { key: 'Security', label: 'Security', colorHex: '#dc2626' },
  { key: 'Documentation', label: 'Documentation', colorHex: '#64748b' },
]

export const TaxonomySettings: React.FC<TaxonomySettingsProps> = ({
  workspaceId,
  isManagerOrAdmin,
}) => {
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [items, setItems] = useState<LocalTaxonomyItem[]>([])

  // New item inputs
  const [newKind, setNewKind] = useState<TaxonomyKind>('domain')
  const [newKey, setNewKey] = useState('')
  const [newLabel, setNewLabel] = useState('')
  const [newColor, setNewColor] = useState('#3b82f6')

  const fetchTaxonomies = useCallback(async () => {
    setLoading(true)
    try {
      const data = await jevApi.getTaxonomies(workspaceId)
      const domainList = (data.domains || []).map((d) => ({
        kind: 'domain' as TaxonomyKind,
        key: d.key,
        label: d.label,
        colorHex: d.colorHex,
        position: d.position,
      }))
      const workTypeList = (data.workTypes || []).map((w) => ({
        kind: 'work_type' as TaxonomyKind,
        key: w.key,
        label: w.label,
        colorHex: w.colorHex,
        position: w.position,
      }))
      setItems([...domainList, ...workTypeList])
    } catch {
      message.error('Không thể tải danh sách taxonomy.')
    } finally {
      setLoading(false)
    }
  }, [workspaceId])

  useEffect(() => {
    let active = true
    if (workspaceId) {
      jevApi
        .getTaxonomies(workspaceId)
        .then((data) => {
          if (!active) return
          const domainList = (data.domains || []).map((d) => ({
            kind: 'domain' as TaxonomyKind,
            key: d.key,
            label: d.label,
            colorHex: d.colorHex,
            position: d.position,
          }))
          const workTypeList = (data.workTypes || []).map((w) => ({
            kind: 'work_type' as TaxonomyKind,
            key: w.key,
            label: w.label,
            colorHex: w.colorHex,
            position: w.position,
          }))
          setItems([...domainList, ...workTypeList])
        })
        .catch(() => {
          if (!active) return
          message.error('Không thể tải danh sách taxonomy.')
        })
    }
    return () => {
      active = false
    }
  }, [workspaceId])

  const handleAddItem = () => {
    const trimmedKey = newKey.trim()
    const trimmedLabel = newLabel.trim() || trimmedKey
    if (!trimmedKey) {
      message.warning('Vui lòng nhập định danh (Key).')
      return
    }

    // Check duplicate
    if (items.some((i) => i.kind === newKind && i.key.toLowerCase() === trimmedKey.toLowerCase())) {
      message.error('Định danh này đã tồn tại trong danh mục.')
      return
    }

    setItems((prev) => [
      ...prev,
      {
        kind: newKind,
        key: trimmedKey,
        label: trimmedLabel,
        colorHex: newColor,
        position: prev.length,
      },
    ])

    setNewKey('')
    setNewLabel('')
  }

  const handleDeleteItem = (kind: TaxonomyKind, key: string) => {
    setItems((prev) => prev.filter((i) => !(i.kind === kind && i.key === key)))
  }

  const handleRestoreDefaults = () => {
    const domainList = DEFAULT_DOMAINS.map((d, idx) => ({
      kind: 'domain' as TaxonomyKind,
      key: d.key,
      label: d.label,
      colorHex: d.colorHex,
      position: idx,
    }))

    const workTypeList = DEFAULT_WORK_TYPES.map((w, idx) => ({
      kind: 'work_type' as TaxonomyKind,
      key: w.key,
      label: w.label,
      colorHex: w.colorHex,
      position: idx,
    }))

    setItems([...domainList, ...workTypeList])
    message.info('Đã tải danh mục chuẩn. Nhấn "Lưu thay đổi" để áp dụng.')
  }

  const handleSave = async () => {
    if (!isManagerOrAdmin) return
    setSaving(true)
    try {
      const domainRequests = items
        .filter((i) => i.kind === 'domain')
        .map((i) => ({ key: i.key, label: i.label, colorHex: i.colorHex }))
      const workTypeRequests = items
        .filter((i) => i.kind === 'work_type')
        .map((i) => ({ key: i.key, label: i.label, colorHex: i.colorHex }))

      await jevApi.replaceTaxonomies(workspaceId, {
        domains: domainRequests,
        workTypes: workTypeRequests,
      })
      message.success('Đã lưu cấu hình phân loại thành công.')
      await fetchTaxonomies()
    } catch {
      message.error('Không thể lưu cấu hình taxonomy.')
    } finally {
      setSaving(false)
    }
  }

  const domains = items.filter((i) => i.kind === 'domain')
  const workTypes = items.filter((i) => i.kind === 'work_type')

  if (loading) {
    return (
      <div style={{ textAlign: 'center', padding: '40px 0' }}>
        <Spin />
      </div>
    )
  }

  return (
    <div data-testid="taxonomy-settings" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Flex justify="space-between" align="center">
        <div>
          <Typography.Title level={5} style={{ margin: 0 }}>
            <TagsOutlined style={{ marginRight: 6 }} />
            Từ điển Phân loại (Taxonomy cho Jev)
          </Typography.Title>
          <Typography.Text type="secondary" style={{ fontSize: 13 }}>
            Định nghĩa các miền kỹ thuật và loại công việc chuẩn để AI Triage phân loại chính xác.
          </Typography.Text>
        </div>

        {isManagerOrAdmin && (
          <Space>
            <Button icon={<ReloadOutlined />} onClick={handleRestoreDefaults}>
              Khôi phục mặc định
            </Button>
            <Button
              type="primary"
              icon={<SaveOutlined />}
              loading={saving}
              onClick={handleSave}
              style={{ backgroundColor: '#6366f1' }}
            >
              Lưu thay đổi
            </Button>
          </Space>
        )}
      </Flex>

      {/* Add New Item Input */}
      {isManagerOrAdmin && (
        <Card size="small" style={{ backgroundColor: '#f8fafc', borderRadius: 8 }}>
          <Typography.Text strong style={{ fontSize: 12, display: 'block', marginBottom: 8 }}>
            Thêm phân loại mới:
          </Typography.Text>
          <Flex gap={8} wrap="wrap" align="center">
            <select
              value={newKind}
              onChange={(e) => setNewKind(e.target.value as TaxonomyKind)}
              style={{
                height: 32,
                borderRadius: 6,
                border: '1px solid #d9d9d9',
                padding: '0 8px',
                fontSize: 13,
              }}
            >
              <option value="domain">Miền kỹ thuật (Domain)</option>
              <option value="work_type">Loại công việc (WorkType)</option>
            </select>

            <Input
              placeholder="Định danh (vd: Backend, Mobile)"
              value={newKey}
              onChange={(e) => setNewKey(e.target.value)}
              style={{ width: 180 }}
            />

            <Input
              placeholder="Nhãn hiển thị (Label)"
              value={newLabel}
              onChange={(e) => setNewLabel(e.target.value)}
              style={{ width: 180 }}
            />

            <input
              type="color"
              value={newColor}
              onChange={(e) => setNewColor(e.target.value)}
              style={{
                width: 36,
                height: 32,
                borderRadius: 6,
                border: '1px solid #d9d9d9',
                cursor: 'pointer',
              }}
            />

            <Button
              icon={<PlusOutlined />}
              onClick={handleAddItem}
              type="dashed"
              style={{ borderRadius: 6 }}
            >
              Thêm
            </Button>
          </Flex>
        </Card>
      )}

      {/* Domain Group */}
      <Card size="small" title={`1. Miền kỹ thuật (Domains - ${domains.length})`} style={{ borderRadius: 8 }}>
        <Flex gap={8} wrap="wrap">
          {domains.map((item) => (
            <Tag
              key={item.key}
              color={item.colorHex || 'blue'}
              closable={isManagerOrAdmin}
              onClose={(e) => {
                e.preventDefault()
                handleDeleteItem('domain', item.key)
              }}
              style={{ fontSize: 13, padding: '4px 10px', borderRadius: 6, display: 'flex', alignItems: 'center', gap: 6 }}
            >
              <span style={{ fontWeight: 600 }}>{item.label}</span>
              <span style={{ opacity: 0.7, fontSize: 11 }}>({item.key})</span>
            </Tag>
          ))}
          {domains.length === 0 && (
            <Typography.Text type="secondary">Chưa có miền kỹ thuật nào.</Typography.Text>
          )}
        </Flex>
      </Card>

      {/* Work Type Group */}
      <Card size="small" title={`2. Loại công việc (Work Types - ${workTypes.length})`} style={{ borderRadius: 8 }}>
        <Flex gap={8} wrap="wrap">
          {workTypes.map((item) => (
            <Tag
              key={item.key}
              color={item.colorHex || 'purple'}
              closable={isManagerOrAdmin}
              onClose={(e) => {
                e.preventDefault()
                handleDeleteItem('work_type', item.key)
              }}
              style={{ fontSize: 13, padding: '4px 10px', borderRadius: 6, display: 'flex', alignItems: 'center', gap: 6 }}
            >
              <span style={{ fontWeight: 600 }}>{item.label}</span>
              <span style={{ opacity: 0.7, fontSize: 11 }}>({item.key})</span>
            </Tag>
          ))}
          {workTypes.length === 0 && (
            <Typography.Text type="secondary">Chưa có loại công việc nào.</Typography.Text>
          )}
        </Flex>
      </Card>
    </div>
  )
}
