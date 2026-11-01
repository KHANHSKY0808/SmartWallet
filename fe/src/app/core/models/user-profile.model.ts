export interface UserProfile {
  userId: string;
  fullName: string;
  avatarUrl: string | null;
  defaultCurrency: string;
  language: string;
  createdAt: string;
  updatedAt: string;
}

export interface UpdateProfileRequest {
  fullName: string;
  defaultCurrency: string;
  language: string;
}

export interface Currency {
  code: string;
  name: string;
  symbol: string;
  isActive: boolean;
}

export interface AvatarUploadResponse {
  avatarUrl: string;
}
