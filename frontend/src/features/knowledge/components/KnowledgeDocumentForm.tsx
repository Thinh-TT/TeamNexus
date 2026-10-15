import React, { useState } from 'react'
import { Button, Form, Input, Modal, Typography } from 'antd'
import { KNOWLEDGE_LIMITS } from '../types/knowledge.types'
import type { CreateKnowledgeDocumentRequest } from '../types/knowledge.types'

interface KnowledgeDocumentFormProps {
  open: boolean
  onCancel: () => void
  /** Resolves `true` when the server accepted the request (an approval is now pending). */
  onSubmit: (request: CreateKnowledgeDocumentRequest) => Promise<boolean>
}

interface KnowledgeDocumentFormValues {
  title: string
  sourceUrl?: string
  content: string
}

/**
 * The "add a document" modal (Phase 14.7 §B).
 *
 * <p>The three caps are the server's own, enforced here as well so a user cannot press a button that is
 * guaranteed to come back as a 400: `maxLength` makes it impossible to type past a limit, `showCount` says
 * how close the content is to the 400.000-character ceiling, and the `max` rules catch a pasted value.</p>
 *
 * <p>The button says "Gửi yêu cầu", not "Thêm": the API records a PENDING action and a Manager still has to
 * approve it. A label promising an added document would be the first of two lies.</p>
 */
export const KnowledgeDocumentForm: React.FC<KnowledgeDocumentFormProps> = ({
  open,
  onCancel,
  onSubmit,
}) => {
  const [form] = Form.useForm<KnowledgeDocumentFormValues>()
  const [submitting, setSubmitting] = useState(false)

  const handleCancel = () => {
    form.resetFields()
    onCancel()
  }

  const handleFinish = async (values: KnowledgeDocumentFormValues) => {
    setSubmitting(true)

    try {
      const accepted = await onSubmit({
        title: values.title.trim(),
        // An empty box means "no source", which the contract expresses as null rather than "".
        sourceUrl: values.sourceUrl?.trim() ? values.sourceUrl.trim() : null,
        content: values.content,
      })

      if (accepted) {
        form.resetFields()
        onCancel()
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="Thêm tài liệu vào bộ nhớ ngữ nghĩa"
      open={open}
      onCancel={handleCancel}
      footer={null}
      // The form is rebuilt on every open, so a cancelled draft cannot reappear in the next modal.
      destroyOnHidden
      width={640}
    >
      <Typography.Paragraph type="secondary" style={{ fontSize: 12.5, marginTop: 8 }}>
        Tài liệu chỉ được ghi vào bộ nhớ sau khi Manager duyệt yêu cầu này.
      </Typography.Paragraph>

      <Form form={form} layout="vertical" onFinish={handleFinish}>
        <Form.Item
          label="Tiêu đề tài liệu"
          name="title"
          rules={[
            { required: true, message: 'Vui lòng nhập tiêu đề tài liệu' },
            {
              max: KNOWLEDGE_LIMITS.title,
              message: `Tiêu đề tối đa ${KNOWLEDGE_LIMITS.title} ký tự`,
            },
          ]}
        >
          <Input
            maxLength={KNOWLEDGE_LIMITS.title}
            showCount
            placeholder="Ví dụ: Quy trình rà soát bảo mật"
            data-testid="knowledge-form-title"
          />
        </Form.Item>

        <Form.Item
          label="Đường dẫn nguồn (không bắt buộc)"
          name="sourceUrl"
          rules={[
            {
              max: KNOWLEDGE_LIMITS.sourceUrl,
              message: `Đường dẫn nguồn tối đa ${KNOWLEDGE_LIMITS.sourceUrl} ký tự`,
            },
          ]}
        >
          <Input
            maxLength={KNOWLEDGE_LIMITS.sourceUrl}
            placeholder="https://example.com/tai-lieu"
            data-testid="knowledge-form-source-url"
          />
        </Form.Item>

        <Form.Item
          label="Nội dung"
          name="content"
          rules={[
            { required: true, message: 'Vui lòng nhập nội dung tài liệu' },
            {
              max: KNOWLEDGE_LIMITS.content,
              message: `Nội dung tối đa ${KNOWLEDGE_LIMITS.content.toLocaleString('vi-VN')} ký tự`,
            },
          ]}
        >
          <Input.TextArea
            rows={8}
            maxLength={KNOWLEDGE_LIMITS.content}
            showCount
            placeholder="Dán hoặc gõ nội dung cần đưa vào bộ nhớ ngữ nghĩa..."
            style={{ borderRadius: 8 }}
            data-testid="knowledge-form-content"
          />
        </Form.Item>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
          <Button onClick={handleCancel}>Huỷ</Button>
          <Button
            type="primary"
            htmlType="submit"
            loading={submitting}
            data-testid="knowledge-form-submit"
            style={{ backgroundColor: '#6366f1' }}
          >
            Gửi yêu cầu
          </Button>
        </div>
      </Form>
    </Modal>
  )
}
