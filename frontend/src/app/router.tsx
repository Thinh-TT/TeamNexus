import React from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from '../features/auth/components/ProtectedRoute'
import { LoginPage } from '../features/auth/pages/LoginPage'
import { BoardListPage } from '../features/board/pages/BoardListPage'
import { BoardPage } from '../features/board/pages/BoardPage'
import { ReportsPage } from '../features/reporting/pages/ReportsPage'
import { WorkspaceSettingsPage } from '../features/workspace/pages/WorkspaceSettingsPage'
import { WorkspaceActivityPage } from '../features/workspace/pages/WorkspaceActivityPage'
import { WorkspaceMembersPage } from '../features/members/pages/WorkspaceMembersPage'
import { KnowledgeBasePage } from '../features/knowledge/components/KnowledgeBasePage'
import { ProfilePage } from '../features/profile/pages/ProfilePage'
import { AcceptInvitationPage } from '../features/invitations/pages/AcceptInvitationPage'
import { WorkspaceDashboardPage } from '../features/dashboard/pages/WorkspaceDashboardPage'
import { TaskSearchPage } from '../features/search/pages/TaskSearchPage'
import { RootHomePage } from '../features/landing/pages/RootHomePage'
import { WorkspaceShell } from '../shared/components/WorkspaceShell'

export const AppRouter: React.FC = () => {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/invitations/accept" element={<AcceptInvitationPage />} />
        <Route path="/" element={<RootHomePage />} />
        <Route
          path="/workspaces/:workspaceId"
          element={
            <ProtectedRoute>
              <WorkspaceShell />
            </ProtectedRoute>
          }
        >
          <Route index element={<Navigate to="dashboard" replace />} />
          <Route path="dashboard" element={<WorkspaceDashboardPage />} />
          <Route path="search" element={<TaskSearchPage />} />
          <Route path="boards" element={<BoardListPage />} />
          <Route path="boards/:boardId" element={<BoardPage />} />
          <Route path="reports" element={<ReportsPage />} />
          <Route path="settings" element={<WorkspaceSettingsPage />} />
          <Route path="activity" element={<WorkspaceActivityPage />} />
          <Route path="members" element={<WorkspaceMembersPage />} />
          <Route path="knowledge" element={<KnowledgeBasePage />} />
        </Route>
        <Route
          path="/profile"
          element={
            <ProtectedRoute>
              <ProfilePage />
            </ProtectedRoute>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
