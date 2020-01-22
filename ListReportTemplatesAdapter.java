package com.ktk.razrezdiagnostics.screens.reportTemplates;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.TextView;

import com.ktk.razrezdiagnostics.R;
import com.ktk.razrezdiagnostics.models.Report;
import com.ktk.razrezdiagnostics.models.ReportTemplate;
import com.ktk.razrezdiagnostics.screens.reportSections.ReportSectionsActivity;

import java.util.ArrayList;
import java.util.Locale;

import se.emilsjolander.stickylistheaders.StickyListHeadersAdapter;

public class ListReportTemplatesAdapter extends BaseAdapter implements StickyListHeadersAdapter {

    private ArrayList<ReportTemplate> allTemplates;
    private ArrayList<ReportTemplate> templatesInList;
    private Context context;
    private LayoutInflater inflater;
    private Activity parentActivity;


    ListReportTemplatesAdapter(ArrayList<ReportTemplate> list, Context context,
                               Activity parentActivity) {
        allTemplates = new ArrayList<>();
        allTemplates.addAll(list);

        templatesInList = new ArrayList<>();
        templatesInList.addAll(list);

        this.context = context;
        this.inflater = LayoutInflater.from(context);
        this.parentActivity = parentActivity;
    }


    @Override
    public int getCount() {
        return templatesInList.size();
    }


    @Override
    public Object getItem(int position) {
        return templatesInList.get(position);
    }


    @Override
    public long getItemId(int position) {
        return 0;
    }


    @Override
    public View getView(final int position, View convertView, ViewGroup parent) {
        ItemViewHolder holder;

        if (convertView == null) {
            LayoutInflater inflater = (LayoutInflater) context.getSystemService(Context.LAYOUT_INFLATER_SERVICE);
            convertView = inflater.inflate(R.layout.list_row_with_subtext, null);
            holder = new ItemViewHolder(convertView);
            convertView.setTag(holder);
        } else {
            holder = (ItemViewHolder) convertView.getTag();
        }

        holder.title.setText(templatesInList.get(position).title);
        holder.subtitle.setText(templatesInList.get(position).getSubtitle());

        convertView.setOnClickListener(v -> {
            Report newReport = new Report(context, templatesInList.get(position));
            Intent intent = new Intent(context, ReportSectionsActivity.class);
            intent.putExtra("REPORT_UUID", newReport.uuid);
            intent.putExtra("ACTIVITY_TITLE", context.getResources().getString(R.string.new_report_title));
            context.startActivity(intent);
            parentActivity.finish();
        });

        return convertView;
    }


    void filter(String searchString) {
        // Lowercase search string
        searchString = searchString.toLowerCase(Locale.getDefault());
        // Clear list
        templatesInList.clear();

        if (searchString.length() == 0) {
            templatesInList.addAll(allTemplates);
        } else {
            for (ReportTemplate item : allTemplates) {
                if (
                        item.title.toLowerCase(Locale.getDefault()).contains(searchString) ||
                                item.type.toLowerCase(Locale.getDefault()).contains(searchString) ||
                                item.getSubtitle().toLowerCase(Locale.getDefault()).contains(searchString)
                ) {
                    templatesInList.add(item);
                }
            }
        }

        notifyDataSetChanged();
    }


    @Override
    public View getHeaderView(int position, View convertView, ViewGroup parent) {
        HeaderViewHolder holder;

        if (convertView == null) {
            convertView = inflater.inflate(R.layout.list_row_header, parent, false);
            holder = new HeaderViewHolder(convertView);
            convertView.setTag(holder);
        } else {
            holder = (HeaderViewHolder) convertView.getTag();
        }

        holder.text.setText(templatesInList.get(position).type);
        return convertView;
    }


    @Override
    public long getHeaderId(int position) {
        return templatesInList.get(position).type.hashCode();
    }


    class HeaderViewHolder {
        TextView text;

        HeaderViewHolder(View view) {
            text = view.findViewById(R.id.label_title);
        }
    }


    class ItemViewHolder {
        TextView title;
        TextView subtitle;

        ItemViewHolder(View view) {
            title = view.findViewById(R.id.label_title);
            subtitle = view.findViewById(R.id.label_subtitle);
        }
    }

}
