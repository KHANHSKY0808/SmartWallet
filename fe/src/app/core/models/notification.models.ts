export interface NotificationDto {
  id: string;
  userId: string;
  type: string;
  title: string;
  message: string;
  isRead: boolean;
  metadata?: string;
  createdAt: string;
}

export interface NotificationFilterParameters {
  page?: number;
  pageSize?: number;
}

