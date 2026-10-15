import React, { useEffect, useState } from 'react'
import {
  Modal,
  Form,
  Select,
  InputNumber,
  Typography,
  message,
  Flex,
  Tag,
  Alert,
} from 'antd'
import { SolutionOutlined, RobotOutlined } from '@ant-design/icons'
import { jevApi } from '../../ai/services/jevApi'
import type { WorkspaceMemberResponse } from '../../board/types/board.types'
import type { UpdateMemberProfileRequest } from '../../ai/types/jev.types'

interface MemberSkillsFormProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  member: WorkspaceMemberResponse | null
  onSuccess?: () => void
}

const PRIMARY_ROLES = [
  'Tech Lead',
  'Backend Dev',
  'Frontend Dev',
  'QA Engineer',
  'Scrum Master',
  'DevOps',
  'Member',
]

const SKILL_SLUG_REGEX = /^[a-z0-9][a-z0-9+._-]{0,39}$/

export const MemberSkillsForm: React.FC<MemberSkillsFormProps> = ({
  open,
  onClose,
  workspaceId,
  member,
  onSuccess,
}) => {
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)

  const isAiAgent = member?.memberType === 'ai_agent'

  useEffect(() => {
    if (member && open) {
      form.setFieldsValue({
        skills: member.skills || [],
        primaryRole: member.primaryRole || 'Member',
        capacityWeight: member.capacityWeight ?? 1.0,
        maxWipLimit: member.maxWipLimit ?? 5,
      })
    }
  }, [member, open, form])

  const handleFinish = async (values: UpdateMemberProfileRequest) => {
    if (!member || isAiAgent) return

    // Validate skills slugs
    const normalizedSkills = (values.skills || [])
      .map((s) => s.trim().toLowerCase())
      .filter(Boolean)

    if (normalizedSkills.length > 20) {
      message.error('Tối đa 20 kỹ năng cho một thành viên.')
      return
    }

    for (const skill of normalizedSkills) {
      if (!SKILL_SLUG_REGEX.test(skill)) {
        message.error(
          `Kỹ năng "${skill}" không hợp lệ. Chỉ chấp nhận chữ thường, số, và ký tự + . _ -`
        )
        return
      }
    }

    setSaving(true)
    try {
      await jevApi.updateMemberProfile(workspaceId, member.userId, {
        skills: Array.from(new Set(normalizedSkills)),
        primaryRole: values.primaryRole || 'Member',
        capacityWeight: Number(values.capacityWeight) || 1.0,
        maxWipLimit: Number(values.maxWipLimit) || 5,
      })
      message.success('Đã cập nhật hồ sơ kỹ năng thành công.')
      onSuccess?.()
      onClose()
    } catch {
      message.error('Không thể cập nhật hồ sơ kỹ năng.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onCancel={onClose}
      title={
        <Flex align="center" gap={8}>
          <SolutionOutlined style={{ color: '#6366f1' }} />
          <span>Hồ sơ năng lực: {member?.displayName}</span>
        </Flex>
      }
      onOk={() => form.submit()}
      okText="Lưu hồ sơ"
      cancelText="Huỷ"
      confirmLoading={saving}
      okButtonProps={{ disabled: isAiAgent, style: { backgroundColor: '#6366f1' } }}
      data-testid="member-skills-modal"
    >
      {isAiAgent ? (
        <Alert
          type="warning"
          showIcon
          icon={<RobotOutlined />}
          title="Không thể chỉnh sửa AI Agent"
          description="Hồ sơ năng lực và kỹ năng của AI Agent được quản lý tự động bởi hệ thống và persona."
          style={{ marginTop: 16 }}
        />
      ) : (
        <Form
          form={form}
          layout="vertical"
          onFinish={handleFinish}
          style={{ marginTop: 16 }}
        >
          <Form.Item
            name="primaryRole"
            label="Vai trò chính (Primary Role)"
            rules={[{ required: true, message: 'Vui lòng chọn vai trò' }]}
          >
            <Select
              options={PRIMARY_ROLES.map((r) => ({ value: r, label: r }))}
              placeholder="Chọn vai trò chính"
            />
          </Form.Item>

          <Form.Item
            name="skills"
            label="Danh sách kỹ năng (Skills / Tags)"
            tooltip="Nhập slug kỹ năng (ví dụ: csharp, react, postgresql, docker). Nhấn Enter để thêm."
          >
            <Select
              mode="tags"
              placeholder="Nhập kỹ năng và nhấn Enter..."
              tokenSeparators={[',', ' ']}
              style={{ width: '100%' }}
              tagRender={(props) => (
                <Tag color="indigo" closable={props.closable} onClose={props.onClose}>
                  {props.label}
                </Tag>
              )}
            />
          </Form.Item>

          <Flex gap={16}>
            <Form.Item
              name="capacityWeight"
              label="Hệ số tải (Capacity Weight)"
              tooltip="Trọng số làm việc (0.1 - 3.0). Mặc định là 1.0 (Full-time)."
              style={{ flex: 1 }}
              rules={[{ required: true, message: 'Nhập hệ số tải' }]}
            >
              <InputNumber
                min={0.1}
                max={3.0}
                step={0.1}
                style={{ width: '100%' }}
              />
            </Form.Item>

            <Form.Item
              name="maxWipLimit"
              label="Giới hạn WIP (Max Open Tasks)"
              tooltip="Số lượng thẻ tối đa đang xử lý đồng thời (0 - 50)."
              style={{ flex: 1 }}
              rules={[{ required: true, message: 'Nhập giới hạn WIP' }]}
            >
              <InputNumber min={0} max={50} step={1} style={{ width: '100%' }} />
            </Form.Item>
          </Flex>

          <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
            Hồ sơ này sẽ được Jev sử dụng để điều phối và đề xuất người phụ trách
            (Assignee Dispatching) tự động dựa trên mức độ phù hợp kỹ năng và tải việc thực tế.
          </Typography.Paragraph>
        </Form>
      )}
    </Modal>
  )
}
