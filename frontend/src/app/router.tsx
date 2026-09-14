import React from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from '../features/auth/components/ProtectedRoute'
import { DashboardPage } from '../features/auth/pages/DashboardPage'
import { LoginPage } from '../features/auth/pages/LoginPage'
import { BoardListPage } from '../features/board/pages/BoardListPage'
import { BoardPage } from '../features/board/pages/BoardPage'
import { ReportsPage } from '../features/reporting/pages/ReportsPage'
import { WorkspaceSettingsPage } from '../features/workspace/pages/WorkspaceSettingsPage'
import { WorkspaceActivityPage } from '../features/workspace/pages/WorkspaceActivityPage'

export const AppRouter: React.FC = () => {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <DashboardPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/workspaces/:workspaceId/boards"
          element={
            <ProtectedRoute>
              <BoardListPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/workspaces/:workspaceId/boards/:boardId"
          element={
            <ProtectedRoute>
              <BoardPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/workspaces/:workspaceId/reports"
          element={
            <ProtectedRoute>
              <ReportsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/workspaces/:workspaceId/settings"
          element={
            <ProtectedRoute>
              <WorkspaceSettingsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/workspaces/:workspaceId/activity"
          element={
            <ProtectedRoute>
              <WorkspaceActivityPage />
            </ProtectedRoute>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
