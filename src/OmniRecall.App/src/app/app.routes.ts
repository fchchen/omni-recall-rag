import { Routes } from '@angular/router';
import { ChatPageComponent } from './pages/chat/chat.page';
import { DocumentsPageComponent } from './pages/documents/documents.page';
import { RecallPageComponent } from './pages/recall/recall.page';
import { UploadPageComponent } from './pages/upload/upload.page';
import { EvalPageComponent } from './pages/eval/eval.page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  { path: 'chat', component: ChatPageComponent },
  { path: 'documents', component: DocumentsPageComponent },
  { path: 'recall', component: RecallPageComponent },
  { path: 'eval', component: EvalPageComponent },
  { path: 'upload', component: UploadPageComponent },
  { path: '**', redirectTo: 'chat' },
];
